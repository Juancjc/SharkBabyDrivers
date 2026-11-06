using System.Runtime.Versioning;
using SharkBabyDrivers.Core.Hid;

namespace SharkBabyDrivers.Core.Devices.X11;

/// <summary>The feature-report connection used by an X11 mouse.</summary>
public interface IX11FeatureTransport : IDisposable
{
    void SetFeature(byte[] report);
    byte[] GetFeature(byte reportId, int length);
}

[SupportedOSPlatform("windows")]
internal sealed class HidX11FeatureTransport(HidConnection connection) : IX11FeatureTransport
{
    public void SetFeature(byte[] report) => connection.SetFeature(report);
    public byte[] GetFeature(byte reportId, int length) => connection.GetFeature(reportId, length);
    public void Dispose() => connection.Dispose();
}
