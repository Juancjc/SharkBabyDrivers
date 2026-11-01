using System.Runtime.InteropServices;

namespace SharkBabyDrivers.Services;

/// <summary>
/// Tells when a key of the Attack Shark keyboard (VID 3151, cable or receiver) is pressed, using Windows Raw Input
/// on the app's (possibly hidden) window. Only the moment of the press is used, never which key; other keyboards
/// and the mouse are ignored. No hook is installed and normal keyboard input is untouched (no RIDEV_NOLEGACY).
/// </summary>
public static class KeyboardActivity
{
    public const int WmInput = 0x00FF;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidInput = 0x10000003;
    private const uint RidiDeviceName = 0x20000007;
    private const uint RimTypeKeyboard = 1;
    private const ushort RiKeyBreak = 0x01;

    private static readonly Dictionary<IntPtr, bool> Known = new();

    /// <summary>Starts receiving keyboard raw input on <paramref name="hwnd"/>, also while it is hidden.</summary>
    public static bool Register(IntPtr hwnd)
    {
        var dev = new RawInputDevice { UsagePage = 0x01, Usage = 0x06, Flags = RidevInputSink, Target = hwnd };
        return RegisterRawInputDevices([dev], 1, (uint)Marshal.SizeOf<RawInputDevice>());
    }

    /// <summary>For a WM_INPUT message: true when it is a key press of the Attack Shark keyboard.</summary>
    public static bool IsAttackSharkKeyDown(IntPtr lParam)
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        if (GetRawInputData(lParam, RidInput, IntPtr.Zero, ref size, headerSize) != 0 || size == 0) return false;
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RidInput, buffer, ref size, headerSize) != size) return false;
            var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
            if (header.Type != RimTypeKeyboard) return false;
            var flags = (ushort)Marshal.ReadInt16(buffer, (int)headerSize + 2); // RAWKEYBOARD.Flags
            if ((flags & RiKeyBreak) != 0) return false;                          // key release
            return IsAttackShark(header.Device);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private static bool IsAttackShark(IntPtr device)
    {
        if (device == IntPtr.Zero) return false;
        lock (Known)
        {
            if (Known.TryGetValue(device, out var known)) return known;
            uint chars = 0;
            GetRawInputDeviceInfo(device, RidiDeviceName, null, ref chars);
            var name = new char[Math.Max(1, (int)chars)];
            var ok = GetRawInputDeviceInfo(device, RidiDeviceName, name, ref chars) > 0;
            var path = ok ? new string(name).TrimEnd('\0') : "";
            var match = path.Contains("VID_3151", StringComparison.OrdinalIgnoreCase);
            Known[device] = match;
            return match;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RawInputDevice[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr rawInput, uint command, IntPtr data, ref uint size, uint headerSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, char[]? data, ref uint size);
}
