namespace SharkBabyDrivers.Core.Hid;

/// <summary>One HID top-level collection as Windows exposes it (one device path per collection).</summary>
public sealed record HidDeviceInfo(
    string Path,
    ushort VendorId,
    ushort ProductId,
    ushort Version,
    ushort UsagePage,
    ushort Usage,
    int InputReportLength,
    int OutputReportLength,
    int FeatureReportLength,
    string Product,
    string Serial)
{
    /// <summary>USB interface number parsed from the path (…&amp;mi_02…), or -1 when the device has a single interface.</summary>
    public int InterfaceNumber
    {
        get
        {
            var i = Path.IndexOf("&mi_", StringComparison.OrdinalIgnoreCase);
            return i >= 0 && i + 6 <= Path.Length && int.TryParse(Path.AsSpan(i + 4, 2), System.Globalization.NumberStyles.HexNumber, null, out var n) ? n : -1;
        }
    }

    /// <summary>Stable key for the physical USB device (all collections of one device share it).</summary>
    public string ContainerKey
    {
        get
        {
            // \\?\hid#vid_1d57&pid_fa60&mi_02&col04#8&2981be81&0&0003#{guid}
            // Instance part differs per collection, but the vid/pid/serial identify the device well enough.
            return $"{VendorId:X4}:{ProductId:X4}:{Serial}";
        }
    }

    public override string ToString() =>
        $"{VendorId:X4}:{ProductId:X4} up={UsagePage:X4} u={Usage:X2} mi={InterfaceNumber} in={InputReportLength} out={OutputReportLength} feat={FeatureReportLength} \"{Product}\"";
}
