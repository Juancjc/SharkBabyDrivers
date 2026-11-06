using System.Runtime.Versioning;
using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Devices.X11;
using SharkBabyDrivers.Core.Devices.R5;
using SharkBabyDrivers.Core.Devices.X68;

namespace SharkBabyDrivers.Core;

/// <summary>Every device family SharkBabyDrivers knows how to drive.</summary>
[SupportedOSPlatform("windows")]
public static class DeviceCatalog
{
    public static IReadOnlyList<IDeviceDriver> Drivers { get; } =
    [
        new X11Driver(),
        new R5Driver(),
        new X68Driver(),
    ];

    public static IReadOnlyList<ushort> VendorIds { get; } = [X11Protocol.VendorId, X68Protocol.VendorId, R5Protocol.VendorId];
}
