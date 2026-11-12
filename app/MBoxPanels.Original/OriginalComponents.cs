// Hosts the original Universal Emulator 1.1.3.0 WinRT components (NesboxPanels.dll, GameController.dll from the
// Store package) in a plain desktop process:
//   - activation goes straight to the DLLs next to this assembly (no package or registration needed);
//   - MBoxPanels.EmulatorPanel, a Windows.UI.Xaml SwapChainPanel, lives in a XAML Island inside a host window
//     that the app embeds with Uno's Win32NativeWindow.
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using MBoxPanels.Contracts;
using Windows.Storage.Streams;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Hosting;
using WinRT;

namespace MBoxPanels.Original;

public sealed class OriginalComponents : IOriginalComponents
{
    private static readonly string Dir = Path.GetDirectoryName(typeof(OriginalComponents).Assembly.Location)!;
    private static readonly Dictionary<string, IntPtr> Modules = new(StringComparer.OrdinalIgnoreCase);
    private static WindowsXamlManager? _xamlManager;

    public OriginalComponents()
    {
        ActivationFactory.ActivationHandler = ActivateFromOriginalDll;
    }

    public IOriginalEmulatorPanel CreateEmulatorPanel()
    {
        _xamlManager ??= WindowsXamlManager.InitializeForCurrentThread();
        return new OriginalEmulatorPanel();
    }

    public IOriginalSevenZip OpenSevenZip(byte[] archive) => new OriginalSevenZip(archive);

    public IOriginalGameController OpenGameController(uint index) => new OriginalGameController(index);

    // ---- activation: DllGetActivationFactory from the original DLLs ----

    private static IntPtr ActivateFromOriginalDll(string typeName, Guid iid)
    {
        string? dll = typeName.StartsWith("MBoxPanels.", StringComparison.Ordinal) ? "NesboxPanels.dll"
            : typeName.StartsWith("GameController.", StringComparison.Ordinal) ? "GameController.dll"
            : null;
        if (dll == null)
            return IntPtr.Zero;

        IntPtr module;
        lock (Modules)
        {
            if (!Modules.TryGetValue(dll, out module))
            {
                // LOAD_WITH_ALTERED_SEARCH_PATH: resolve the UWP C++ runtime (*_app.dll) from this folder too.
                module = Native.LoadLibraryExW(Path.Combine(Dir, dll), IntPtr.Zero, 0x00000008);
                if (module == IntPtr.Zero)
                    throw new DllNotFoundException($"{dll}: error {Marshal.GetLastWin32Error()}");
                Modules[dll] = module;
            }
        }

        var getFactory = Marshal.GetDelegateForFunctionPointer<Native.DllGetActivationFactory>(
            Native.GetProcAddress(module, "DllGetActivationFactory"));
        Marshal.ThrowExceptionForHR(Native.WindowsCreateString(typeName, typeName.Length, out var hstring));
        try
        {
            Marshal.ThrowExceptionForHR(getFactory(hstring, out var factory));
            try
            {
                Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory, in iid, out var result));
                return result;
            }
            finally { Marshal.Release(factory); }
        }
        finally { Native.WindowsDeleteString(hstring); }
    }
}

internal sealed class OriginalEmulatorPanel : IOriginalEmulatorPanel
{
    private const string HostClass = "UniversalEmulator.OriginalPanelHost";
    private static Native.WndProc? _wndProc;

    private readonly MBoxPanels.EmulatorPanel _panel;
    private readonly DesktopWindowXamlSource _source;
    private readonly IntPtr _host;
    private IntPtr _island;
    private readonly List<MBoxPanels.IGameController> _controllers = new();
    private bool _disposed;

    public event Action<int>? StateChanged;

    public OriginalEmulatorPanel()
    {
        _host = CreateHostWindow();

        _source = new DesktopWindowXamlSource();
        var native = _source.As<IDesktopWindowXamlSourceNative>();
        native.AttachToWindow(_host);
        native.GetWindowHandle(out _island);
        Native.SetProp(_host, "Island", _island);

        // Same layout as the original EmulatorPage: the panel centred over a black page.
        _panel = new MBoxPanels.EmulatorPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _panel.StateChanged += (_, state) => StateChanged?.Invoke((int)state);
        _source.Content = new Grid
        {
            Background = new Windows.UI.Xaml.Media.SolidColorBrush(Windows.UI.Colors.Black),
            IsHitTestVisible = false,
            Children = { _panel },
        };

        // Input stays with the app (the original page drove the panel from its own key/gamepad handlers).
        Native.EnableWindow(_host, false);
    }

    public nint WindowHandle => _host;

    public void LoadRom(string name, byte[] rom, byte[]? state)
    {
        if (state is { Length: > 0 })
            _panel.LoadRom(name, rom.AsBuffer(), state.AsBuffer());
        else
            _panel.LoadRom(name, rom.AsBuffer());
    }

    public void UnloadRom() => _panel.UnloadRom();

    public byte[] SaveState() => _panel.SaveState()?.ToArray() ?? Array.Empty<byte>();

    public void LoadState(byte[] state)
    {
        if (state is { Length: > 0 })
            _panel.LoadState(state.AsBuffer());
    }

    public byte[] SaveSnapshot()
    {
        using var stream = new InMemoryRandomAccessStream();
        _panel.SaveSnapshotToStream(stream);
        if (stream.Size == 0)
            return Array.Empty<byte>();
        stream.Seek(0);
        using var net = stream.AsStreamForRead();
        using var ms = new MemoryStream();
        net.CopyTo(ms);
        return ms.ToArray();
    }

    public void SetControl(int controlKey, bool pressed) => _panel.SetControl((MBoxPanels.ControlKey)controlKey, pressed);

    public void SetControls(int mask) => _panel.SetControls(mask);

    public void AddController(Func<uint> getControlsState)
    {
        var controller = new DelegateGameController(getControlsState);
        _controllers.Add(controller); // keep the CCW alive for as long as the panel uses it
        _panel.AddController(controller);
    }

    public bool IsSoundEnabled { get => _panel.IsSoundEnabled; set => _panel.IsSoundEnabled = value; }
    public bool IsBlurEnabled { get => _panel.IsBlurEnabled; set => _panel.IsBlurEnabled = value; }
    public bool IsEmulationPaused { get => _panel.IsEmulationPaused; set => _panel.IsEmulationPaused = value; }
    public int VideoMode { get => (int)_panel.VideoMode; set => _panel.VideoMode = (MBoxPanels.VideoModeType)value; }
    public int LoadedEmulator => (int)_panel.LoadedEmulator;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try { _panel.UnloadRom(); } catch { }
        try { _panel.Dispose(); } catch { }
        _source.Content = null;
        _source.Dispose();
        Native.DestroyWindow(_host);
    }

    private static IntPtr CreateHostWindow()
    {
        IntPtr hinstance = Native.GetModuleHandleW(null);
        if (_wndProc == null)
        {
            _wndProc = HostWndProc;
            var wc = new Native.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<Native.WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hinstance,
                hbrBackground = Native.GetStockObject(4 /* BLACK_BRUSH */),
                lpszClassName = HostClass,
            };
            Native.RegisterClassExW(ref wc);
        }

        const uint WS_POPUP = 0x80000000, WS_CLIPCHILDREN = 0x02000000;
        return Native.CreateWindowExW(0, HostClass, "", WS_POPUP | WS_CLIPCHILDREN, 0, 0, 320, 240,
            IntPtr.Zero, IntPtr.Zero, hinstance, IntPtr.Zero);
    }

    private static IntPtr HostWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_SIZE = 0x0005, WM_ERASEBKGND = 0x0014;
        if (msg == WM_SIZE)
        {
            IntPtr island = Native.GetProp(hwnd, "Island");
            if (island != IntPtr.Zero)
            {
                int w = (int)(lParam.ToInt64() & 0xFFFF), h = (int)((lParam.ToInt64() >> 16) & 0xFFFF);
                Native.SetWindowPos(island, IntPtr.Zero, 0, 0, w, h, 0x0054 /* NOZORDER|NOACTIVATE|SHOWWINDOW */);
            }
            return IntPtr.Zero;
        }
        if (msg == WM_ERASEBKGND)
            return 1;
        return Native.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    private sealed partial class DelegateGameController(Func<uint> getControlsState) : MBoxPanels.IGameController
    {
        public uint GetControlsState()
        {
            try { return getControlsState(); }
            catch { return 0; }
        }
    }
}

internal sealed class OriginalSevenZip(byte[] archive) : IOriginalSevenZip
{
    private readonly MBoxPanels.SevenZipWrapper _wrapper = new(archive.AsBuffer());

    public IReadOnlyList<string> List() => _wrapper.list().ToList();

    public byte[] Extract(string name) => _wrapper.extract(name)?.ToArray() ?? Array.Empty<byte>();
}

internal sealed class OriginalGameController(uint index) : IOriginalGameController
{
    private readonly GameController.Controller _controller = new(index);

    public OriginalPadState GetState()
    {
        var s = _controller.GetState();
        return new OriginalPadState
        {
            controllerId = s.controllerId, packetNumber = s.packetNumber, buttons = s.buttons, connected = s.connected,
            a = s.a, b = s.b, x = s.x, y = s.y,
            dpad_up = s.dpad_up, dpad_down = s.dpad_down, dpad_left = s.dpad_left, dpad_right = s.dpad_right,
            left_thumb = s.left_thumb, right_thumb = s.right_thumb,
            left_shoulder = s.left_shoulder, right_shoulder = s.right_shoulder,
            start = s.start, back = s.back,
            LeftTrigger = s.LeftTrigger, RightTrigger = s.RightTrigger,
            LeftThumbX = s.LeftThumbX, LeftThumbY = s.LeftThumbY, RightThumbX = s.RightThumbX, RightThumbY = s.RightThumbY,
        };
    }

    public void SetState(ushort leftSpeed, ushort rightSpeed) => _controller.SetState(leftSpeed, rightSpeed);
}

[ComImport, Guid("3cbcf1bf-2f76-4e9c-96ab-e84b37972554"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDesktopWindowXamlSourceNative
{
    void AttachToWindow(IntPtr parentWnd);
    void GetWindowHandle(out IntPtr hWnd);
}

internal static class Native
{
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int DllGetActivationFactory(IntPtr activatableClassId, out IntPtr factory);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);
    [DllImport("kernel32", CharSet = CharSet.Ansi)]
    public static extern IntPtr GetProcAddress(IntPtr module, string name);
    [DllImport("kernel32", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("combase", CharSet = CharSet.Unicode)]
    public static extern int WindowsCreateString(string s, int length, out IntPtr hstring);
    [DllImport("combase")]
    public static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("user32", CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW wc);
    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateWindowExW(uint exStyle, string cls, string name, uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32")]
    public static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32")]
    public static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32")]
    public static extern bool EnableWindow(IntPtr hwnd, bool enable);
    [DllImport("user32")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32", CharSet = CharSet.Unicode)]
    public static extern bool SetProp(IntPtr hwnd, string name, IntPtr data);
    [DllImport("user32", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetProp(IntPtr hwnd, string name);
    [DllImport("gdi32")]
    public static extern IntPtr GetStockObject(int obj);
}
