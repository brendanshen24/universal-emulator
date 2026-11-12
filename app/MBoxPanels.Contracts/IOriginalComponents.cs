namespace MBoxPanels.Contracts;

/// <summary>
/// The original Universal Emulator 1.1.3.0 WinRT components (NesboxPanels.dll, GameController.dll), loaded from
/// the Store package's own binaries. Enum-typed values use the original numbering, which the app's enums match
/// (EmulatorStateType, EmulatorType, VideoModeType, ControlKey).
/// </summary>
public interface IOriginalComponents
{
    /// <summary>Creates the original MBoxPanels.EmulatorPanel. Must be called on the UI thread.</summary>
    IOriginalEmulatorPanel CreateEmulatorPanel();

    /// <summary>MBoxPanels.SevenZipWrapper over an in-memory .7z archive.</summary>
    IOriginalSevenZip OpenSevenZip(byte[] archive);

    /// <summary>GameController.Controller (XInput pad <paramref name="index"/>).</summary>
    IOriginalGameController OpenGameController(uint index);
}

public interface IOriginalEmulatorPanel : IDisposable
{
    /// <summary>Top-level host window containing the panel; give it to Uno's Win32NativeWindow.</summary>
    nint WindowHandle { get; }

    /// <summary>EmulatorStateChanged, with the original EmulatorStateType value.</summary>
    event Action<int>? StateChanged;

    void LoadRom(string name, byte[] rom, byte[]? state);
    void UnloadRom();
    byte[] SaveState();
    void LoadState(byte[] state);
    /// <summary>SaveSnapshotToStream: PNG of the current frame.</summary>
    byte[] SaveSnapshot();
    void SetControl(int controlKey, bool pressed);
    void SetControls(int mask);
    /// <summary>AddController with an IGameController whose GetControlsState() calls <paramref name="getControlsState"/>.</summary>
    void AddController(Func<uint> getControlsState);

    bool IsSoundEnabled { get; set; }
    bool IsBlurEnabled { get; set; }
    bool IsEmulationPaused { get; set; }
    int VideoMode { get; set; }
    int LoadedEmulator { get; }
}

public interface IOriginalSevenZip
{
    IReadOnlyList<string> List();
    byte[] Extract(string name);
}

/// <summary>GameController.State, field for field.</summary>
public struct OriginalPadState
{
    public uint controllerId;
    public uint packetNumber;
    public ushort buttons;
    public bool connected;
    public bool a, b, x, y;
    public bool dpad_up, dpad_down, dpad_left, dpad_right;
    public bool left_thumb, right_thumb, left_shoulder, right_shoulder;
    public bool start, back;
    public byte LeftTrigger, RightTrigger;
    public short LeftThumbX, LeftThumbY, RightThumbX, RightThumbY;
}

public interface IOriginalGameController
{
    OriginalPadState GetState();
    void SetState(ushort leftSpeed, ushort rightSpeed);
}
