using SharkBabyDrivers.Core.Devices.X11;
using static SharkBabyDrivers.Core.Devices.X11.X11Protocol;

namespace SharkBabyDrivers.Tests;

/// <summary>An isolated feature-report connection with controllable RF loss and read responses.</summary>
internal sealed class ScriptedX11Transport : IX11FeatureTransport
{
    private readonly Dictionary<byte, byte[]> _reports = new();
    private byte? _pendingRead;

    public List<byte[]> Sent { get; } = new();
    public IEnumerable<byte[]> Writes => Sent.Where(report => report[0] != RidRead);
    public bool Awake { get; set; } = true;
    public bool Disposed { get; private set; }
    public byte[]? Buttons { get; private set; }
    public Dictionary<(byte Slot, byte Page), byte[]> MacroPages { get; } = new();

    /// <summary>Return false to silently drop a write; throw HidException to simulate a USB transfer error.</summary>
    public Func<byte[], bool>? AcceptWrite { get; set; }

    /// <summary>Optionally replace a reply, e.g. to return a stale configuration during verification.</summary>
    public Func<byte, byte[], byte[]>? ReadReply { get; set; }

    public X11Config Config => new(_reports[RidDpi], _reports[RidLight], _reports[RidPoll]);

    public ScriptedX11Transport(X11Config? initial = null)
    {
        _reports[RidVersion] = [RidVersion, LenVersion, 0x01, 0x09, 0, 0, 0, 0];
        ReplaceConfig(initial ?? X11Config.FactoryDefault());
    }

    /// <summary>Changes physical state without going through the driver's cached editor state.</summary>
    public void ReplaceConfig(X11Config config)
    {
        _reports[RidDpi] = config.DpiFrame();
        _reports[RidLight] = config.LightFrame();
        _reports[RidPoll] = config.PollFrame();
    }

    public void SetFeature(byte[] report)
    {
        var copy = (byte[])report.Clone();
        Sent.Add(copy);
        if (copy[0] == RidRead)
        {
            _pendingRead = copy[1];
            return;
        }
        if (!Awake || AcceptWrite?.Invoke(copy) == false) return;
        switch (copy[0])
        {
            case RidDpi:
                if (!DpiChecksumOk(copy)) throw new InvalidDataException("Invalid DPI checksum.");
                _reports[RidDpi] = copy;
                break;
            case RidLight:
                if (!LightChecksumOk(copy)) throw new InvalidDataException("Invalid lighting checksum.");
                _reports[RidLight] = copy;
                break;
            case RidPoll:
                if ((byte)(copy[3] ^ copy[4]) != 0xFF) throw new InvalidDataException("Invalid polling complement.");
                _reports[RidPoll] = copy;
                break;
            case RidButtons:
                Buttons = copy;
                break;
            case RidMacro:
                MacroPages[(copy[2], copy[3])] = copy;
                break;
            case RidReset:
                ReplaceConfig(X11Config.FactoryDefault());
                Buttons = null;
                MacroPages.Clear();
                break;
            default:
                throw new InvalidOperationException($"Unexpected write to report 0x{copy[0]:X2}.");
        }
    }

    public byte[] GetFeature(byte reportId, int length)
    {
        if (reportId == RidRead) return [RidRead, (byte)(Awake ? 1 : 0), 0, 0, 0, 0, 0, 0];
        if (_pendingRead != reportId) throw new InvalidOperationException("Configuration reads require an A0 handshake.");
        if (!_reports.TryGetValue(reportId, out var stored)) throw new InvalidOperationException("The report is write-only.");
        var result = stored[..length];
        return ReadReply?.Invoke(reportId, result) ?? result;
    }

    public void Dispose() => Disposed = true;
}
