using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using GameController;
using MBox.Model;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace MBoxPanels
{
    public sealed class EmulatorPanel : Grid, IDisposable
    {
        private const int SampleRate = 44100;

        public static readonly DependencyProperty IsSoundEnabledProperty =
            DependencyProperty.Register(nameof(IsSoundEnabled), typeof(bool), typeof(EmulatorPanel),
                new PropertyMetadata(true, OnSoundChanged));

        public static readonly DependencyProperty IsBlurEnabledProperty =
            DependencyProperty.Register(nameof(IsBlurEnabled), typeof(bool), typeof(EmulatorPanel),
                new PropertyMetadata(false, OnBlurChanged));

        public static readonly DependencyProperty IsEmulationPausedProperty =
            DependencyProperty.Register(nameof(IsEmulationPaused), typeof(bool), typeof(EmulatorPanel),
                new PropertyMetadata(false, OnPausedChanged));

        public static readonly DependencyProperty VideoModeProperty =
            DependencyProperty.Register(nameof(VideoMode), typeof(VideoModeType), typeof(EmulatorPanel),
                new PropertyMetadata(VideoModeType.Unknown, OnVideoModeChanged));

        public static readonly DependencyProperty LoadedEmulatorProperty =
            DependencyProperty.Register(nameof(LoadedEmulator), typeof(EmulatorType), typeof(EmulatorPanel),
                new PropertyMetadata(EmulatorType.Unknown));

        public event EventHandler<EmulatorStateType>? StateChanged;

        private readonly VideoCanvas _canvas;
        private readonly object _bitmapLock = new();
        private SkiaSharp.SKBitmap? _skBitmap;
        private readonly object _sync = new();
        private readonly List<Controller> _controllers = new();

        private IntPtr _core;
        private int _width;
        private int _height;
        private int _framerate = 60;
        private byte[]? _frameBytes;
        private byte[]? _lastFrameBytes;
        private int _lastWidth;
        private int _lastHeight;
        private uint _manualControlMask;

        private DispatcherTimer? _timer;
        private readonly Stopwatch _clock = new();
        private double _accumulator;
        private long _lastTicks;
        private bool _audioOpened;
        private bool _disposed;

        public EmulatorPanel()
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Black);
            _canvas = new VideoCanvas(this)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Children.Add(_canvas);
            Unloaded += (_, _) => StopLoop();
        }

        public bool IsSoundEnabled
        {
            get => (bool)GetValue(IsSoundEnabledProperty);
            set => SetValue(IsSoundEnabledProperty, value);
        }

        public bool IsBlurEnabled
        {
            get => (bool)GetValue(IsBlurEnabledProperty);
            set => SetValue(IsBlurEnabledProperty, value);
        }

        public bool IsEmulationPaused
        {
            get => (bool)GetValue(IsEmulationPausedProperty);
            set => SetValue(IsEmulationPausedProperty, value);
        }

        public VideoModeType VideoMode
        {
            get => (VideoModeType)GetValue(VideoModeProperty);
            set => SetValue(VideoModeProperty, value);
        }

        public EmulatorType LoadedEmulator
        {
            get => (EmulatorType)GetValue(LoadedEmulatorProperty);
            set => SetValue(LoadedEmulatorProperty, value);
        }

        public EmulatorStateType State { get; private set; } = EmulatorStateType.NoRom;

        private static void OnSoundChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EmulatorPanel panel && !(bool)e.NewValue && panel._audioOpened)
                NativeMethods.nb_audio_clear();
        }

        private static void OnBlurChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EmulatorPanel panel)
                panel._canvas.Invalidate();
        }

        private static void OnPausedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EmulatorPanel panel)
            {
                if ((bool)e.NewValue && panel._audioOpened)
                    NativeMethods.nb_audio_clear();
                panel._lastTicks = 0;
                panel._accumulator = 0;
            }
        }

        private static void OnVideoModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is EmulatorPanel panel)
            {
                var mode = (VideoModeType)e.NewValue;
                if (mode == VideoModeType.PAL)
                    panel._framerate = 50;
                else if (mode == VideoModeType.NTSC)
                    panel._framerate = 60;
            }
        }

        public void AddController(Controller controller)
        {
            if (controller == null) return;
            lock (_sync)
            {
                if (_controllers.All(c => c.Id != controller.Id))
                    _controllers.Add(controller);
            }
        }

        public void SetControl(ControlKey key, bool pressed)
        {
            uint bit = 1u << (int)key;
            lock (_sync)
            {
                if (pressed)
                    _manualControlMask |= bit;
                else
                    _manualControlMask &= ~bit;
            }
        }

        public void SetControls(int mask)
        {
            lock (_sync)
                _manualControlMask = (uint)mask;
        }

        public void LoadRom(string fileName, byte[] romData, byte[]? stateData = null)
        {
            UnloadRom();

            if (string.IsNullOrEmpty(fileName) || romData == null || romData.Length == 0)
            {
                SetState(EmulatorStateType.Error);
                return;
            }

            if (!_audioOpened)
            {
                _audioOpened = NativeMethods.nb_audio_open(SampleRate) != 0;
            }

            IntPtr core = NativeMethods.nb_create(fileName, SampleRate);
            if (core == IntPtr.Zero)
            {
                SetState(EmulatorStateType.Error);
                return;
            }

            if (NativeMethods.nb_load_rom(core, romData, romData.Length) == 0)
            {
                NativeMethods.nb_destroy(core);
                SetState(EmulatorStateType.Error);
                return;
            }

            NativeMethods.nb_info(core, out int rawType, out int width, out int height, out int framerate, out int pal);

            lock (_sync)
            {
                _core = core;
                _width = Math.Max(width, 1);
                _height = Math.Max(height, 1);
                _framerate = framerate > 0 ? framerate : (pal != 0 ? 50 : 60);
                _frameBytes = new byte[_width * _height * 4];
                _lastWidth = _width;
                _lastHeight = _height;
                _lastFrameBytes = new byte[_frameBytes.Length];
            }

            LoadedEmulator = MapEmulatorType(rawType);
            if (VideoMode == VideoModeType.Unknown)
                VideoMode = pal != 0 ? VideoModeType.PAL : VideoModeType.NTSC;

            if (stateData != null && stateData.Length > 0)
                NativeMethods.nb_load_state(core, stateData, stateData.Length);

            lock (_bitmapLock)
            {
                _skBitmap?.Dispose();
                _skBitmap = new SkiaSharp.SKBitmap(
                    new SkiaSharp.SKImageInfo(_width, _height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Opaque));
            }

            SetState(EmulatorStateType.RomLoaded);
            StartLoop();
        }

        public void UnloadRom()
        {
            StopLoop();
            lock (_sync)
            {
                if (_core != IntPtr.Zero)
                {
                    NativeMethods.nb_destroy(_core);
                    _core = IntPtr.Zero;
                }
                _manualControlMask = 0;
            }

            if (_audioOpened)
                NativeMethods.nb_audio_clear();

            LoadedEmulator = EmulatorType.Unknown;
            SetState(EmulatorStateType.NoRom);
        }

        public byte[] SaveState()
        {
            lock (_sync)
            {
                if (_core == IntPtr.Zero)
                    return Array.Empty<byte>();

                int size = NativeMethods.nb_save_state(_core, out var ptr);
                if (size <= 0 || ptr == IntPtr.Zero)
                    return Array.Empty<byte>();

                var buf = new byte[size];
                Marshal.Copy(ptr, buf, 0, size);
                return buf;
            }
        }

        public void LoadState(byte[] data)
        {
            if (data == null || data.Length == 0)
                return;

            lock (_sync)
            {
                if (_core != IntPtr.Zero)
                    NativeMethods.nb_load_state(_core, data, data.Length);
            }
        }

        public async Task SaveSnapshotToStream(Stream stream)
        {
            byte[]? bgraCopy;
            int w;
            int h;
            lock (_sync)
            {
                bgraCopy = _lastFrameBytes?.ToArray();
                w = _lastWidth;
                h = _lastHeight;
            }

            if (bgraCopy == null || w <= 0 || h <= 0)
                return;

            byte[] png = EncodePngFromBgra(bgraCopy, w, h);
            await stream.WriteAsync(png, 0, png.Length).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }

        public async Task SaveSnapshotToStream(IRandomAccessStream stream)
        {
            using var netStream = stream.AsStreamForWrite();
            await SaveSnapshotToStream(netStream).ConfigureAwait(false);
        }

        private void StartLoop()
        {
            _clock.Restart();
            _lastTicks = _clock.ElapsedTicks;
            _accumulator = 0;

            if (_timer == null)
            {
                _timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(8),
                };
                _timer.Tick += OnTimerTick;
            }
            _timer.Start();
        }

        private void StopLoop()
        {
            _timer?.Stop();
            _clock.Stop();
        }

        private void OnTimerTick(object? sender, object e)
        {
            if (_disposed || _core == IntPtr.Zero || IsEmulationPaused)
                return;

            long now = _clock.ElapsedTicks;
            double elapsed = (double)(now - _lastTicks) / Stopwatch.Frequency;
            _lastTicks = now;

            if (elapsed > 0.1)
                elapsed = 0.1;

            _accumulator += elapsed;
            double step = 1.0 / Math.Max(30, _framerate);
            int maxSteps = 3;
            bool rendered = false;

            while (_accumulator >= step && maxSteps-- > 0)
            {
                _accumulator -= step;
                rendered |= StepFrame();
            }

            if (rendered)
                PresentFrame();
        }

        private bool StepFrame()
        {
            uint p1 = _manualControlMask;
            foreach (var c in _controllers)
                p1 |= (uint)AppSettings.BuildControllerMask(c);

            IntPtr fb = NativeMethods.nb_tick(_core, p1, 0);
            int sampleCount = NativeMethods.nb_sound(_core, out var samplesPtr);

            if (IsSoundEnabled && _audioOpened && sampleCount > 0 && samplesPtr != IntPtr.Zero)
            {
                if (NativeMethods.nb_audio_queued() < SampleRate / 4)
                    NativeMethods.nb_audio_write(samplesPtr, sampleCount);
            }

            if (fb == IntPtr.Zero || _frameBytes == null)
                return false;

            int byteCount = _width * _height * 4;
            Marshal.Copy(fb, _frameBytes, 0, byteCount);

            // nb_tick outputs 0x00RRGGBB pixels; ensure alpha byte (offset +3 in LE BGRA) is 0xFF.
            for (int i = 3; i < byteCount; i += 4)
                _frameBytes[i] = 0xFF;

            lock (_sync)
            {
                if (_lastFrameBytes == null || _lastFrameBytes.Length != byteCount)
                    _lastFrameBytes = new byte[byteCount];
                System.Buffer.BlockCopy(_frameBytes, 0, _lastFrameBytes, 0, byteCount);
                _lastWidth = _width;
                _lastHeight = _height;
            }

            return true;
        }

        private void PresentFrame()
        {
            if (_frameBytes == null)
                return;

            lock (_bitmapLock)
            {
                if (_skBitmap == null)
                    return;

                nint ptr = _skBitmap.GetPixels();
                if (ptr != IntPtr.Zero)
                    System.Runtime.InteropServices.Marshal.Copy(_frameBytes, 0, ptr, _frameBytes.Length);
            }
            _canvas.Invalidate();
        }

        private sealed class VideoCanvas : Uno.WinUI.Graphics2DSK.SKCanvasElement
        {
            private readonly EmulatorPanel _panel;

            public VideoCanvas(EmulatorPanel panel) => _panel = panel;

            protected override void RenderOverride(SkiaSharp.SKCanvas canvas, Windows.Foundation.Size area)
            {
                canvas.Clear(SkiaSharp.SKColors.Black);
                double w = area.Width;
                double h = area.Height;
                if (w <= 0 || h <= 0)
                    return;

                lock (_panel._bitmapLock)
                {
                    var bmp = _panel._skBitmap;
                    if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0)
                        return;

                    float scale = (float)Math.Min(w / bmp.Width, h / bmp.Height);
                    float dw = bmp.Width * scale;
                    float dh = bmp.Height * scale;
                    float dx = (float)((w - dw) * 0.5);
                    float dy = (float)((h - dh) * 0.5);

                    var src = new SkiaSharp.SKRect(0, 0, bmp.Width, bmp.Height);
                    var dst = new SkiaSharp.SKRect(dx, dy, dx + dw, dy + dh);
                    var filter = _panel.IsBlurEnabled ? SkiaSharp.SKFilterMode.Linear : SkiaSharp.SKFilterMode.Nearest;
                    var sampling = new SkiaSharp.SKSamplingOptions(filter, SkiaSharp.SKMipmapMode.None);
                    canvas.DrawBitmap(bmp, src, dst, sampling, null);
                }
            }
        }

        private void SetState(EmulatorStateType next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(this, next);
        }

        private static EmulatorType MapEmulatorType(int nesboxType) => nesboxType switch
        {
            // libnesbox EmulatorType::Type: SEGA=0, SNES=1, NES=2, GBA=3, GBX=4, UNKNOWN=5
            0 => EmulatorType.Sega,
            1 => EmulatorType.Snes,
            2 => EmulatorType.Nes,
            3 => EmulatorType.Gba,
            4 => EmulatorType.Gbx,
            _ => EmulatorType.Unknown,
        };

        private static byte[] EncodePngFromBgra(byte[] bgra, int width, int height)
        {
            int stride = width * 4;
            byte[] scanlines = new byte[(stride + 1) * height];
            for (int y = 0; y < height; y++)
            {
                int dstRow = y * (stride + 1);
                scanlines[dstRow] = 0; // filter None
                int srcRow = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int si = srcRow + x * 4;
                    int di = dstRow + 1 + x * 4;
                    scanlines[di] = bgra[si + 2];     // R
                    scanlines[di + 1] = bgra[si + 1]; // G
                    scanlines[di + 2] = bgra[si];     // B
                    scanlines[di + 3] = 255;          // A
                }
            }

            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
                zlib.Write(scanlines, 0, scanlines.Length);

            using var ms = new MemoryStream();
            ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

            Span<byte> ihdr = stackalloc byte[13];
            BinaryPrimitives.WriteInt32BigEndian(ihdr[0..4], width);
            BinaryPrimitives.WriteInt32BigEndian(ihdr[4..8], height);
            ihdr[8] = 8;  // bit depth
            ihdr[9] = 6;  // RGBA
            ihdr[10] = 0; // compression
            ihdr[11] = 0; // filter
            ihdr[12] = 0; // interlace
            WriteChunk(ms, "IHDR"u8, ihdr);
            WriteChunk(ms, "IDAT"u8, compressed.ToArray());
            WriteChunk(ms, "IEND"u8, ReadOnlySpan<byte>.Empty);
            return ms.ToArray();
        }

        private static void WriteChunk(Stream ms, ReadOnlySpan<byte> type, ReadOnlySpan<byte> payload)
        {
            Span<byte> buf = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buf, payload.Length);
            ms.Write(buf);
            ms.Write(type);
            ms.Write(payload);
            uint crc = ComputeCrc32(type, payload);
            BinaryPrimitives.WriteUInt32BigEndian(buf, crc);
            ms.Write(buf);
        }

        private static uint ComputeCrc32(ReadOnlySpan<byte> header, ReadOnlySpan<byte> body)
        {
            uint crc = 0xFFFFFFFFu;
            crc = UpdateCrc(crc, header);
            crc = UpdateCrc(crc, body);
            return ~crc;
        }

        private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return crc;
        }

        public new void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            UnloadRom();
            GC.SuppressFinalize(this);
        }
    }
}
