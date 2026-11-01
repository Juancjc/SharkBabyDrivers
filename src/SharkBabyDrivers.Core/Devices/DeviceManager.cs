using System.Runtime.Versioning;
using SharkBabyDrivers.Core.Hid;

namespace SharkBabyDrivers.Core.Devices;

/// <summary>Finds supported devices among the present HID collections and keeps the open set in sync.</summary>
[SupportedOSPlatform("windows")]
public sealed class DeviceManager : IDisposable
{
    private readonly IReadOnlyList<IDeviceDriver> _drivers;
    private readonly Dictionary<string, ISharkDevice> _open = new();
    private readonly object _lock = new();
    private readonly HashSet<ushort> _vendors;

    public event Action<ISharkDevice>? Connected;
    public event Action<ISharkDevice>? Disconnected;
    public event Action<string, Exception>? OpenFailed;

    public DeviceManager(IReadOnlyList<IDeviceDriver> drivers, IEnumerable<ushort> vendorIds)
    {
        _drivers = drivers;
        _vendors = vendorIds.ToHashSet();
    }

    public IReadOnlyList<ISharkDevice> Devices
    {
        get { lock (_lock) return _open.Values.ToList(); }
    }

    /// <summary>Groups HID collections by physical device (vid:pid:serial).</summary>
    public static IEnumerable<IGrouping<string, HidDeviceInfo>> GroupByDevice(IEnumerable<HidDeviceInfo> collections) =>
        collections.GroupBy(c => c.ContainerKey);

    /// <summary>Rescans the bus; opens new devices and drops the ones that went away.</summary>
    public void Refresh()
    {
        var present = HidEnumerator.Enumerate((v, _) => _vendors.Contains(v));
        var seen = new HashSet<string>();
        foreach (var group in GroupByDevice(present))
        {
            var collections = group.ToList();
            var driver = _drivers.FirstOrDefault(d => d.Matches(collections));
            if (driver is null) continue;
            seen.Add(group.Key);
            lock (_lock)
            {
                if (_open.ContainsKey(group.Key)) continue;
            }
            try
            {
                var device = driver.Open(collections);
                lock (_lock) _open[group.Key] = device;
                Connected?.Invoke(device);
            }
            catch (Exception ex)
            {
                OpenFailed?.Invoke(driver.Model, ex);
            }
        }
        List<KeyValuePair<string, ISharkDevice>> gone;
        lock (_lock)
        {
            gone = _open.Where(kv => !seen.Contains(kv.Key)).ToList();
            foreach (var kv in gone) _open.Remove(kv.Key);
        }
        foreach (var kv in gone)
        {
            Disconnected?.Invoke(kv.Value);
            kv.Value.Dispose();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var d in _open.Values) d.Dispose();
            _open.Clear();
        }
    }
}
