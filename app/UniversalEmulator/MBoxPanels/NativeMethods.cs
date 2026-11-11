using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace MBoxPanels
{
    public enum VideoModeType
    {
        Unknown = 0,
        PAL = 1,
        NTSC = 2,
    }

    public enum EmulatorStateType
    {
        NoRom = 0,
        RomLoaded = 1,
        Error = 2,
    }

    public enum EmulatorType
    {
        Unknown = 0,
        Nes = 1,
        Snes = 2,
        Sega = 3,
        Gbx = 4,
        Gba = 5,
    }

    public enum ControlKey
    {
        Up = 0,
        Down = 1,
        Left = 2,
        Right = 3,
        Start = 4,
        Select = 5,
        Mode = 6,
        A = 7,
        B = 8,
        C = 9,
        L = 10,
        R = 11,
        X = 12,
        Y = 13,
        Z = 14,
    }

    internal static class NativeMethods
    {
        private const string LibraryName = "mboxpanels";

        [StructLayout(LayoutKind.Sequential)]
        public struct NativePad
        {
            public ushort Buttons;
            public short ThumbLX;
            public short ThumbLY;
            public byte LeftTrigger;
            public byte RightTrigger;
        }

        [ModuleInitializer]
        internal static void RegisterResolver()
        {
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(NativeMethods).Assembly, ResolveLibrary);
            }
            catch (InvalidOperationException)
            {
                // Already registered for this assembly.
            }
        }

        private static IntPtr ResolveLibrary(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
        {
            if (!string.Equals(libraryName, LibraryName, StringComparison.Ordinal))
                return IntPtr.Zero;

            string fileName = OperatingSystem.IsWindows()
                ? "mboxpanels.dll"
                : OperatingSystem.IsMacOS()
                    ? "libmboxpanels.dylib"
                    : "libmboxpanels.so";

            string baseDir = AppContext.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDir, fileName),
                Path.Combine(baseDir, "..", "Frameworks", fileName),
                Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "native", fileName)),
                Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "native", fileName)),
                Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "native", fileName)),
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                    return handle;
            }

            return NativeLibrary.TryLoad(fileName, assembly, searchPath, out var fallback) ? fallback : IntPtr.Zero;
        }

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr nb_create(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            int sampleRate);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nb_load_rom(IntPtr core, byte[] data, int size);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void nb_info(
            IntPtr core,
            out int type,
            out int width,
            out int height,
            out int framerate,
            out int pal);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr nb_tick(IntPtr core, uint player1, uint player2);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nb_sound(IntPtr core, out IntPtr samples);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nb_save_state(IntPtr core, out IntPtr data);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nb_load_state(IntPtr core, byte[] data, int size);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void nb_destroy(IntPtr core);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr nb_7z_open(byte[] data, int size);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr nb_7z_list(IntPtr archive);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nb_7z_extract(
            IntPtr archive,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
            out IntPtr data);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void nb_7z_close(IntPtr archive);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nb_audio_open(int sampleRate);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void nb_audio_write(IntPtr samples, int count);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nb_audio_queued();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void nb_audio_clear();

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int nb_pad_state(int index, out NativePad state);
    }

    public sealed class SevenZipWrapper : IDisposable
    {
        private IntPtr _handle;

        public SevenZipWrapper(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new ArgumentException("Archive buffer is empty.", nameof(data));

            _handle = NativeMethods.nb_7z_open(data, data.Length);
            if (_handle == IntPtr.Zero)
                throw new InvalidDataException("Failed to open 7z archive.");
        }

        public IReadOnlyList<string> list()
        {
            if (_handle == IntPtr.Zero)
                return Array.Empty<string>();

            var ptr = NativeMethods.nb_7z_list(_handle);
            var joined = Marshal.PtrToStringUTF8(ptr);
            if (string.IsNullOrEmpty(joined))
                return Array.Empty<string>();

            return joined
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        public byte[] extract(string name)
        {
            if (_handle == IntPtr.Zero || string.IsNullOrEmpty(name))
                return Array.Empty<byte>();

            int len = NativeMethods.nb_7z_extract(_handle, name, out var dataPtr);
            if (len <= 0 || dataPtr == IntPtr.Zero)
                return Array.Empty<byte>();

            var result = new byte[len];
            Marshal.Copy(dataPtr, result, 0, len);
            return result;
        }

        public void Dispose()
        {
            if (_handle != IntPtr.Zero)
            {
                NativeMethods.nb_7z_close(_handle);
                _handle = IntPtr.Zero;
            }
            GC.SuppressFinalize(this);
        }

        ~SevenZipWrapper() => Dispose();
    }
}

namespace GameController
{
    [Flags]
    public enum ControllerKey : uint
    {
        None = 0,
        DPAD_UP = 1,
        DPAD_DOWN = 2,
        DPAD_LEFT = 4,
        DPAD_RIGHT = 8,
        START = 16,
        BACK = 32,
        LEFT_THUMB = 64,
        RIGHT_THUMB = 128,
        LEFT_SHOULDER = 256,
        RIGHT_SHOULDER = 512,
        A = 4096,
        B = 8192,
        X = 16384,
        Y = 32768,
        LEFT_TRIGGER = 65536,
        RIGHT_TRIGGER = 131072,
    }

    public struct State
    {
        public uint controllerId;
        public uint packetNumber;
        public ushort buttons;
        public bool connected;
        public bool a;
        public bool b;
        public bool x;
        public bool y;
        public bool dpad_up;
        public bool dpad_down;
        public bool dpad_left;
        public bool dpad_right;
        public bool left_thumb;
        public bool right_thumb;
        public bool left_shoulder;
        public bool right_shoulder;
        public bool start;
        public bool back;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }

    public sealed class Controller
    {
        public uint Id { get; }

        public Controller(uint id = 0)
        {
            Id = id;
        }

        public State GetState()
        {
            int connected = MBoxPanels.NativeMethods.nb_pad_state((int)Id, out var native);
            if (connected == 0)
                return default;

            ushort b = native.Buttons;
            return new State
            {
                controllerId = Id,
                connected = true,
                buttons = b,
                dpad_up = (b & (ushort)ControllerKey.DPAD_UP) != 0,
                dpad_down = (b & (ushort)ControllerKey.DPAD_DOWN) != 0,
                dpad_left = (b & (ushort)ControllerKey.DPAD_LEFT) != 0,
                dpad_right = (b & (ushort)ControllerKey.DPAD_RIGHT) != 0,
                start = (b & (ushort)ControllerKey.START) != 0,
                back = (b & (ushort)ControllerKey.BACK) != 0,
                left_thumb = (b & (ushort)ControllerKey.LEFT_THUMB) != 0,
                right_thumb = (b & (ushort)ControllerKey.RIGHT_THUMB) != 0,
                left_shoulder = (b & (ushort)ControllerKey.LEFT_SHOULDER) != 0,
                right_shoulder = (b & (ushort)ControllerKey.RIGHT_SHOULDER) != 0,
                a = (b & (ushort)ControllerKey.A) != 0,
                b = (b & (ushort)ControllerKey.B) != 0,
                x = (b & (ushort)ControllerKey.X) != 0,
                y = (b & (ushort)ControllerKey.Y) != 0,
                LeftTrigger = native.LeftTrigger,
                RightTrigger = native.RightTrigger,
                LeftThumbX = native.ThumbLX,
                LeftThumbY = native.ThumbLY,
            };
        }
    }
}
