using System.Runtime.Versioning;
using SharkBabyDrivers.Core.Hid;

namespace SharkBabyDrivers.Core.Devices;

public enum DeviceKind { Mouse, Keyboard }

public enum LinkKind { Wired, Dongle, Bluetooth }

/// <summary>Everything that could be read from a device's onboard memory, as raw reports. This is what a backup stores.</summary>
public sealed class DeviceSnapshot
{
    public string Model { get; set; } = "";
    public string DeviceKey { get; set; } = "";
    public ushort VendorId { get; set; }
    public ushort ProductId { get; set; }
    public string Firmware { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string AppVersion { get; set; } = "";
    /// <summary>Raw reports keyed by a stable name (e.g. "report_04", "profile_0_keys"), hex-encoded.</summary>
    public Dictionary<string, string> Reports { get; set; } = new();
    /// <summary>Things that are not readable from the device but are worth keeping (notes, decoded summary).</summary>
    public Dictionary<string, string> Notes { get; set; } = new();

    public void Put(string name, ReadOnlySpan<byte> data) => Reports[name] = Convert.ToHexString(data);
    public byte[]? Get(string name) => Reports.TryGetValue(name, out var hex) ? Convert.FromHexString(hex) : null;
}

/// <summary>A connected, supported peripheral.</summary>
public interface ISharkDevice : IDisposable
{
    /// <summary>Stable key for this physical device (used for backups and profiles).</summary>
    string DeviceKey { get; }
    string Model { get; }
    DeviceKind Kind { get; }
    LinkKind Link { get; }
    ushort VendorId { get; }
    ushort ProductId { get; }

    /// <summary>Reads every readable onboard setting into a snapshot. Must not change anything on the device.</summary>
    Task<DeviceSnapshot> ReadSnapshotAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    /// <summary>Writes a snapshot previously produced by <see cref="ReadSnapshotAsync"/> back to the device.</summary>
    Task RestoreSnapshotAsync(DeviceSnapshot snapshot, IProgress<string>? progress = null, CancellationToken ct = default);
}

/// <summary>Recognizes a supported device among the HID collections of one physical USB device and opens it.</summary>
[SupportedOSPlatform("windows")]
public interface IDeviceDriver
{
    string Model { get; }
    bool Matches(IReadOnlyList<HidDeviceInfo> collections);
    ISharkDevice Open(IReadOnlyList<HidDeviceInfo> collections);
}
