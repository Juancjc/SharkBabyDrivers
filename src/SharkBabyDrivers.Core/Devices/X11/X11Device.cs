using System.Runtime.Versioning;
using SharkBabyDrivers.Core.Hid;

namespace SharkBabyDrivers.Core.Devices.X11;

/// <summary>The 2.4 GHz receiver is present but the mouse is not answering (asleep, off or out of range).</summary>
public sealed class MouseAsleepException() : DeviceAsleepException("O mouse não respondeu. Mexa ou clique no mouse para acordá-lo e tente de novo.");

[SupportedOSPlatform("windows")]
public sealed class X11Driver : IDeviceDriver
{
    public string Model => "Attack Shark X11";

    public bool Matches(IReadOnlyList<HidDeviceInfo> collections) =>
        collections.Any(c => c.VendorId == X11Protocol.VendorId
                             && c.ProductId is X11Protocol.ProductIdDongle or X11Protocol.ProductIdWired
                             && c.UsagePage == X11Protocol.ConfigUsagePage
                             && c.FeatureReportLength > 0);

    public ISharkDevice Open(IReadOnlyList<HidDeviceInfo> collections) => new X11Device(collections);
}

/// <summary>
/// Attack Shark X11 over USB cable (1D57:FA55) or 2.4 GHz dongle (1D57:FA60).
/// All feature I/O goes through the vendor collection (usage page 0x0B); battery and
/// DPI-button events arrive on the usage page 0x0A collection.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class X11Device : ISharkDevice
{
    private readonly HidConnection _config;
    private readonly HidConnection? _events;
    private readonly SemaphoreSlim _busy = new(1, 1);

    public string DeviceKey { get; }
    public string Model => "Attack Shark X11";
    public DeviceKind Kind => DeviceKind.Mouse;
    public LinkKind Link { get; }
    public ushort VendorId => X11Protocol.VendorId;
    public ushort ProductId { get; }

    /// <summary>Raised from a background thread when the mouse reports battery or a DPI-button press.</summary>
    public event Action<X11Event>? EventReceived;

    /// <summary>Delay used by the official app between consecutive transfers.</summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromMilliseconds(120);

    public X11Device(IReadOnlyList<HidDeviceInfo> collections)
    {
        var cfg = collections.First(c => c.UsagePage == X11Protocol.ConfigUsagePage && c.FeatureReportLength > 0);
        ProductId = cfg.ProductId;
        Link = cfg.ProductId == X11Protocol.ProductIdWired ? LinkKind.Wired : LinkKind.Dongle;
        DeviceKey = $"x11-{cfg.VendorId:x4}{cfg.ProductId:x4}{(string.IsNullOrWhiteSpace(cfg.Serial) ? "" : "-" + cfg.Serial)}".ToLowerInvariant();
        _config = HidConnection.Open(cfg);

        var ev = collections.FirstOrDefault(c => c.UsagePage == X11Protocol.EventUsagePage && c.InputReportLength > 0);
        if (ev is not null)
        {
            try
            {
                _events = HidConnection.Open(ev, overlapped: true);
                _events.InputReport += OnInput;
                _events.StartInputLoop();
            }
            catch (HidException)
            {
                _events = null; // events are a nicety; configuration still works without them
            }
        }
    }

    private void OnInput(byte[] report)
    {
        var e = X11Protocol.ParseEvent(report);
        if (e is not null) EventReceived?.Invoke(e);
    }

    // ------------------------------------------------------------------ low level

    /// <summary>How long a read keeps retrying while the mouse is asleep (the dongle answers "status 0" until it wakes).</summary>
    public TimeSpan WakeTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Raised (from the I/O thread) while waiting for a sleeping mouse to answer.</summary>
    public event Action? WaitingForWake;

    /// <summary>
    /// A0 handshake then the report itself (the same sequence the official app uses). Over 2.4 GHz the receiver
    /// answers by itself with status 0 while the mouse sleeps, so this keeps asking until the mouse wakes up.
    /// </summary>
    public byte[] ReadReport(byte rid, int length, byte flag, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? WakeTimeout);
        Exception? last = null;
        var attempt = 0;
        do
        {
            try
            {
                Thread.Sleep(Settle);
                _config.SetFeature(X11Protocol.BuildReadRequest(rid, length, flag));
                Thread.Sleep(TimeSpan.FromMilliseconds(attempt == 0 ? 250 : 400));
                var status = _config.GetFeature(X11Protocol.RidRead, X11Protocol.LenRead);
                if (status[1] == 0x01)
                {
                    var data = _config.GetFeature(rid, length);
                    if (data[0] == rid && (rid == X11Protocol.RidVersion || data[1] == length)) return data;
                    last = new IOException($"resposta inesperada ao ler 0x{rid:X2}: {Convert.ToHexString(data.AsSpan(0, Math.Min(8, data.Length)))}");
                }
                else
                {
                    last = new MouseAsleepException();
                    if (attempt == 1) WaitingForWake?.Invoke();
                }
            }
            catch (HidException ex)
            {
                last = ex;
            }
            attempt++;
        } while (DateTime.UtcNow < deadline);
        if (last is MouseAsleepException) throw new MouseAsleepException();
        throw new IOException($"Não foi possível ler o report 0x{rid:X2} do mouse.", last);
    }

    private void Send(byte[] frame)
    {
        Exception? last = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                _config.SetFeature(frame);
                Thread.Sleep(TimeSpan.FromMilliseconds(200));
                return;
            }
            catch (HidException ex)
            {
                last = ex;
                Thread.Sleep(TimeSpan.FromMilliseconds(200));
            }
        }
        throw new IOException($"Falha ao gravar o report 0x{frame[0]:X2} no mouse.", last);
    }

    /// <summary>Sends a readable report and reads it back; over 2.4 GHz a write is sometimes dropped, so it is resent.</summary>
    private void SendVerified(byte[] frame, int compareFrom, int compareCount)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            Send(frame);
            var back = ReadReport(frame[0], frame[1], 0x01, TimeSpan.FromSeconds(8));
            if (back.AsSpan(compareFrom, compareCount).SequenceEqual(frame.AsSpan(compareFrom, compareCount))) return;
            Thread.Sleep(TimeSpan.FromMilliseconds(300 * (attempt + 1)));
        }
        throw new IOException($"O mouse não confirmou a gravação do report 0x{frame[0]:X2}. Tente novamente perto do receptor.");
    }

    // ------------------------------------------------------------------ high level

    public string ReadFirmware()
    {
        var v = ReadReport(X11Protocol.RidVersion, X11Protocol.LenVersion, 0x00);
        return $"{v[2]:X2}{v[3]:X2}";
    }

    public X11Config ReadConfig()
    {
        var poll = ReadReport(X11Protocol.RidPoll, X11Protocol.LenPoll, 0x01);
        var light = ReadReport(X11Protocol.RidLight, X11Protocol.LenLight, 0x01);
        var dpi = ReadReport(X11Protocol.RidDpi, X11Protocol.LenDpi, 0x01);
        return new X11Config(dpi, light, poll);
    }

    /// <summary>Writes only the reports that differ from <paramref name="current"/> (all of them when it is null).</summary>
    public void WriteConfig(X11Config desired, X11Config? current)
    {
        var dpiChanged = current is null || !desired.DpiFrame().AsSpan(3, 47).SequenceEqual(current.DpiFrame().AsSpan(3, 47));
        var lightChanged = current is null || !desired.LightFrame().AsSpan(3, 8).SequenceEqual(current.LightFrame().AsSpan(3, 8));
        var pollChanged = current is null || desired.Poll[3] != current.Poll[3];

        // The firmware only applies 0x05 after it has seen 0x04, so DPI goes first whenever lighting changes.
        if (dpiChanged || lightChanged)
        {
            SendVerified(desired.DpiFrame(), 3, 46); // byte 49 (one-shot animation) is not echoed reliably
            Thread.Sleep(TimeSpan.FromMilliseconds(300));
        }
        if (lightChanged || dpiChanged)
            SendVerified(desired.LightFrame(), 3, 10);
        if (pollChanged)
            SendVerified(desired.PollFrame(), 3, 2);
    }

    public void WriteButtons(IReadOnlyList<ButtonAction> slots) => Send(X11Buttons.BuildReport(slots));

    public void WriteMacro(int slotId, MacroDefinition macro)
    {
        foreach (var page in X11Macro.Paginate(X11Macro.BuildLogical(slotId, macro)))
            Send(page);
    }

    /// <summary>Factory reset (report 0x0C), exactly as the official "reset" button sends it.</summary>
    public void FactoryReset() => Send([X11Protocol.RidReset, 0x0A, 0x01, 0xFE, 0x01, 0xFE, 0x00, 0x00, 0x00, 0x00]);

    // ------------------------------------------------------------------ async wrappers

    public async Task<T> RunAsync<T>(Func<T> work, CancellationToken ct = default)
    {
        await _busy.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(work, ct).ConfigureAwait(false); }
        finally { _busy.Release(); }
    }

    public Task RunAsync(Action work, CancellationToken ct = default) => RunAsync(() => { work(); return true; }, ct);

    public Task<DeviceSnapshot> ReadSnapshotAsync(IProgress<string>? progress = null, CancellationToken ct = default) =>
        RunAsync(() =>
        {
            var snap = new DeviceSnapshot { Model = Model, DeviceKey = DeviceKey, VendorId = VendorId, ProductId = ProductId };
            foreach (var (rid, len, flag) in X11Protocol.ReadableReports)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"report 0x{rid:X2}");
                var data = ReadReport(rid, len, flag);
                snap.Put($"report_{rid:x2}", data);
                if (rid == X11Protocol.RidVersion) snap.Firmware = $"{data[2]:X2}{data[3]:X2}";
            }
            var cfg = new X11Config(snap.Get("report_04")!, snap.Get("report_05")!, snap.Get("report_06")!);
            snap.Notes["summary"] = Describe(cfg);
            snap.Notes["buttons"] = "O firmware do X11 não permite ler os botões nem as macros; eles ficam guardados no perfil do SharkBabyDrivers.";
            return snap;
        }, ct);

    public Task RestoreSnapshotAsync(DeviceSnapshot snapshot, IProgress<string>? progress = null, CancellationToken ct = default) =>
        RunAsync(() =>
        {
            var dpi = snapshot.Get("report_04") ?? throw new InvalidDataException("backup sem report 0x04");
            var light = snapshot.Get("report_05") ?? throw new InvalidDataException("backup sem report 0x05");
            var poll = snapshot.Get("report_06") ?? throw new InvalidDataException("backup sem report 0x06");
            progress?.Report("DPI / iluminação / polling");
            WriteConfig(new X11Config(dpi, light, poll), null);
        }, ct);

    public static string Describe(X11Config c)
    {
        var stages = string.Join(", ", Enumerable.Range(0, X11Protocol.Stages).Where(c.IsStageEnabled).Select(i => $"{i + 1}:{c.GetStageDpi(i)}{(c.ActiveStage == i + 1 ? "*" : "")}"));
        return $"DPI [{stages}] · {c.PollingHz} Hz · luz {c.LightMode} {c.LightColor} brilho {c.Brightness} vel {c.Speed} · " +
               $"debounce {c.DebounceMs} ms · sono {c.SleepMinutes} min / profundo {c.DeepSleepMinutes} min · " +
               $"angle snap {(c.AngleSnap ? "on" : "off")} · ripple {(c.Ripple ? "on" : "off")}";
    }

    public void Dispose()
    {
        _events?.Dispose();
        _config.Dispose();
        _busy.Dispose();
    }
}
