using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Input;
using MBox.Model;
using MBoxPanels;
using MSACL.UI;
using Windows.Storage;

namespace MBox.ViewModel
{
    public sealed class EmulatorPageViewModel : ViewModelBase
    {
        private readonly SemaphoreSlim _stateLock = new(1, 1);

        private EmulatorPanel? _panel;
        private Rom? _rom;
        private bool _isXBoxMode;
        private bool _isTouchAvailable = true;
        private bool _processGameController = true;
        private bool _isSoundEnabled = AppSettings.IsSoundEnabled;
        private bool _isFullScreenEnabled = AppSettings.IsFullScreenEnabled;
        private bool _isBlurEnabled = AppSettings.IsBlurEnabled;
        private bool _isGamePadAvailable = true;
        private bool _isGamePadEnabled = AppSettings.IsVirtualGamePadEnabled;
        private bool _isSetupGamepadMode;
        private VideoModeType _videoMode = VideoModeType.Unknown;

        public EmulatorPageViewModel()
        {
            ToggleSoundCommand = new RelayCommand(() => IsSoundEnabled = !IsSoundEnabled);
            ToggleFullScreenCommand = new RelayCommand(() => IsFullScreenEnabled = !IsFullScreenEnabled);
            ToggleBlurCommand = new RelayCommand(() => IsBlurEnabled = !IsBlurEnabled);
            ToggleGamePadCommand = new RelayCommand(() => IsGamePadEnabled = !IsGamePadEnabled);
            ToggleVideoModeCommand = new RelayCommand(() =>
            {
                VideoMode = VideoMode == VideoModeType.PAL ? VideoModeType.NTSC : VideoModeType.PAL;
            });
        }

        public event EventHandler<bool>? FullScreenChanged;

        public ICommand ToggleSoundCommand { get; }
        public ICommand ToggleFullScreenCommand { get; }
        public ICommand ToggleBlurCommand { get; }
        public ICommand ToggleGamePadCommand { get; }
        public ICommand ToggleVideoModeCommand { get; }

        public bool IsXBoxMode
        {
            get => _isXBoxMode;
            set => SetProperty(ref _isXBoxMode, value);
        }

        public bool IsTouchAvailable
        {
            get => _isTouchAvailable;
            set => SetProperty(ref _isTouchAvailable, value);
        }

        public bool ProcessGameController
        {
            get => _processGameController;
            set => SetProperty(ref _processGameController, value);
        }

        public bool IsSoundEnabled
        {
            get => _isSoundEnabled;
            set
            {
                if (SetProperty(ref _isSoundEnabled, value))
                    AppSettings.IsSoundEnabled = value;
            }
        }

        public bool IsFullScreenEnabled
        {
            get => _isFullScreenEnabled;
            set
            {
                if (SetProperty(ref _isFullScreenEnabled, value))
                {
                    AppSettings.IsFullScreenEnabled = value;
                    FullScreenChanged?.Invoke(this, value);
                }
            }
        }

        public bool IsBlurEnabled
        {
            get => _isBlurEnabled;
            set
            {
                if (SetProperty(ref _isBlurEnabled, value))
                    AppSettings.IsBlurEnabled = value;
            }
        }

        public bool IsGamePadAvailable
        {
            get => _isGamePadAvailable;
            set => SetProperty(ref _isGamePadAvailable, value);
        }

        public bool IsGamePadEnabled
        {
            get => _isGamePadEnabled;
            set
            {
                if (SetProperty(ref _isGamePadEnabled, value))
                    AppSettings.IsVirtualGamePadEnabled = value;
            }
        }

        public bool IsSetupGamepadMode
        {
            get => _isSetupGamepadMode;
            set
            {
                if (SetProperty(ref _isSetupGamepadMode, value))
                {
                    if (_panel != null)
                        _panel.IsEmulationPaused = value;
                }
            }
        }

        public VideoModeType VideoMode
        {
            get => _videoMode;
            set
            {
                if (SetProperty(ref _videoMode, value))
                {
                    if (_panel != null)
                        _panel.VideoMode = value;
                    OnPropertyChanged(nameof(IsVideoModeUnknown));
                    OnPropertyChanged(nameof(IsNTSC));
                    OnPropertyChanged(nameof(VideoModeName));
                }
            }
        }

        public bool IsVideoModeUnknown => VideoMode == VideoModeType.Unknown;

        public bool IsNTSC => VideoMode == VideoModeType.NTSC;

        // Preserves original spelling ("NTCS") observed in Nesbox.dll.
        public string VideoModeName => VideoMode == VideoModeType.PAL ? "PAL" : "NTCS";

        public void AttachPanel(EmulatorPanel panel)
        {
            _panel = panel;
        }

        public async Task<bool> LoadRomAsync(Rom rom, bool loadSave)
        {
            if (_panel == null)
                return false;

            _rom = rom;
            await _stateLock.WaitAsync();
            try
            {
                var library = ((App)Microsoft.UI.Xaml.Application.Current).Library;
                byte[] romBytes = await library.ReadRomAsync(rom);
                if (romBytes.Length == 0)
                {
                    await AppHelpers.ShowMessageAsync(
                        AppHelpers.GetString("FailedToLoadRomError", "Failed to load the ROM."));
                    return false;
                }

                byte[]? stateBytes = loadSave ? library.ReadState(rom) : null;
                string fileName = (rom.Path ?? string.Empty).Split('/').Last();
                _panel.LoadRom(fileName, romBytes, stateBytes);

                if (_panel.State == EmulatorStateType.Error)
                {
                    await AppHelpers.ShowMessageAsync(
                        AppHelpers.GetString("FailedToLoadRomError", "Failed to load the ROM."));
                    return false;
                }

                VideoMode = _panel.VideoMode;
                await library.MarkPlayedAsync(rom);
                return true;
            }
            catch
            {
                await AppHelpers.ShowMessageAsync(
                    AppHelpers.GetString("FailedToLoadRomError", "Failed to load the ROM."));
                return false;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        public async Task<bool> SaveStateAsync()
        {
            if (_panel == null || _rom == null || _panel.State != EmulatorStateType.RomLoaded)
                return false;

            await _stateLock.WaitAsync();
            try
            {
                byte[] stateBytes = _panel.SaveState();
                if (stateBytes.Length == 0)
                    return false;

                byte[] pngBytes;
                using (var ms = new MemoryStream())
                {
                    await _panel.SaveSnapshotToStream(ms);
                    pngBytes = ms.ToArray();
                }

                var library = ((App)Microsoft.UI.Xaml.Application.Current).Library;
                await library.SaveStateAsync(_rom, stateBytes, pngBytes);
                await library.EnsureThumbnailAsync(_rom, CaptureSnapshotBytesAsync);
                return true;
            }
            finally
            {
                _stateLock.Release();
            }
        }

        public async Task LoadStateAsync()
        {
            if (_panel == null || _rom == null)
                return;

            await _stateLock.WaitAsync();
            try
            {
                var library = ((App)Microsoft.UI.Xaml.Application.Current).Library;
                byte[]? stateBytes = library.ReadState(_rom);
                if (stateBytes != null && stateBytes.Length > 0)
                    _panel.LoadState(stateBytes);
            }
            finally
            {
                _stateLock.Release();
            }
        }

        public async Task EnsureThumbnailAsync()
        {
            if (_panel == null || _rom == null || _panel.State != EmulatorStateType.RomLoaded)
                return;

            await _stateLock.WaitAsync();
            try
            {
                var library = ((App)Microsoft.UI.Xaml.Application.Current).Library;
                await library.EnsureThumbnailAsync(_rom, CaptureSnapshotBytesAsync);
            }
            catch
            {
                // Ignore thumbnail capture failures on exit.
            }
            finally
            {
                _stateLock.Release();
            }
        }

        private async Task<byte[]> CaptureSnapshotBytesAsync()
        {
            if (_panel == null)
                return Array.Empty<byte>();

            using var ms = new MemoryStream();
            await _panel.SaveSnapshotToStream(ms);
            return ms.ToArray();
        }
    }
}
