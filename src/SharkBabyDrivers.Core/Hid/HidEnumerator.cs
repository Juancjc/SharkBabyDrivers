using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static SharkBabyDrivers.Core.Hid.NativeMethods;

namespace SharkBabyDrivers.Core.Hid;

[SupportedOSPlatform("windows")]
public static class HidEnumerator
{
    /// <summary>Lists every present HID collection, optionally filtered by vendor id.</summary>
    public static IReadOnlyList<HidDeviceInfo> Enumerate(Func<ushort, ushort, bool>? vidPidFilter = null)
    {
        var result = new List<HidDeviceInfo>();
        HidD_GetHidGuid(out var guid);
        var set = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;
        try
        {
            for (var index = 0; ; index++)
            {
                var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data)) break;
                var path = GetPath(set, ref data);
                if (path is null) continue;
                if (vidPidFilter is not null && TryParseVidPid(path, out var v, out var p) && !vidPidFilter(v, p)) continue;
                var info = Describe(path);
                if (info is null) continue;
                if (vidPidFilter is not null && !vidPidFilter(info.VendorId, info.ProductId)) continue;
                result.Add(info);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return result;
    }

    private static string? GetPath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data)
    {
        SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out var size, IntPtr.Zero);
        if (size <= 0) return null;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            // cbSize of SP_DEVICE_INTERFACE_DETAIL_DATA_W: 8 on x64, 6 on x86.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, buffer, size, out _, IntPtr.Zero)) return null;
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    internal static bool TryParseVidPid(string path, out ushort vid, out ushort pid)
    {
        vid = pid = 0;
        var lower = path.ToLowerInvariant();
        var v = lower.IndexOf("vid_", StringComparison.Ordinal);
        var p = lower.IndexOf("pid_", StringComparison.Ordinal);
        if (v < 0 || p < 0 || v + 8 > lower.Length || p + 8 > lower.Length) return false;
        return ushort.TryParse(lower.AsSpan(v + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out vid)
            && ushort.TryParse(lower.AsSpan(p + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out pid);
    }

    /// <summary>Opens the collection without read/write access (always allowed) to read its attributes and caps.</summary>
    public static HidDeviceInfo? Describe(string path)
    {
        using var handle = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;
        var attributes = new HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HIDD_ATTRIBUTES>() };
        if (!HidD_GetAttributes(handle, ref attributes)) return null;
        var caps = new HIDP_CAPS();
        if (HidD_GetPreparsedData(handle, out var preparsed))
        {
            try { HidP_GetCaps(preparsed, ref caps); }
            finally { HidD_FreePreparsedData(preparsed); }
        }
        return new HidDeviceInfo(path, attributes.VendorID, attributes.ProductID, attributes.VersionNumber,
            caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength,
            ReadString(handle, HidD_GetProductString), ReadString(handle, HidD_GetSerialNumberString));
    }

    private delegate bool StringGetter(Microsoft.Win32.SafeHandles.SafeFileHandle h, char[] buffer, int length);

    private static string ReadString(Microsoft.Win32.SafeHandles.SafeFileHandle handle, StringGetter getter)
    {
        var buffer = new char[128];
        return getter(handle, buffer, buffer.Length * 2) ? new string(buffer).TrimEnd('\0') : string.Empty;
    }
}
