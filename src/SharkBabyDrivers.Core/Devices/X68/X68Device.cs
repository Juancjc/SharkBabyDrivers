using System.Runtime.Versioning;
using SharkBabyDrivers.Core.Hid;
using static SharkBabyDrivers.Core.Devices.X68.X68Protocol;

namespace SharkBabyDrivers.Core.Devices.X68;

[SupportedOSPlatform("windows")]
public sealed class X68Driver : IDeviceDriver
{
    public string Model => "Attack Shark X68 PRO HE";

    public bool Matches(IReadOnlyList<HidDeviceInfo> collections) =>
        collections.Any(c => c.VendorId == VendorId
                             && c.UsagePage == ConfigUsagePage && c.Usage == ConfigUsage && c.FeatureReportLength > 0
                             && c.ProductId is PidX68ProHeWired or PidX68HeWired or PidDongle);

    public ISharkDevice Open(IReadOnlyList<HidDeviceInfo> collections)
    {
        var cfg = collections.First(c => c.UsagePage == ConfigUsagePage && c.Usage == ConfigUsage && c.FeatureReportLength > 0);
        if (cfg.ProductId != PidDongle) return new X68Device(collections);
        // Through the 2.4 GHz receiver: same protocol, relayed (and paced) by the receiver.
        var key = $"x68-dongle-{cfg.VendorId:x4}{cfg.ProductId:x4}".ToLowerInvariant();
        return new X68Device(new DongleX68Transport(collections), key, cfg.ProductId, LinkKind.Dongle) { TransferDelayMs = 0 };
    }
}

/// <summary>X68 PRO HE over the USB cable (3151:502F) or its 2.4 GHz receiver (3151:5026).</summary>
[SupportedOSPlatform("windows")]
public sealed class X68Device : ISharkDevice
{
    private readonly IX68Transport _io;
    private readonly SemaphoreSlim _busy = new(1, 1);
    private volatile bool _deviceBusy;
    private uint? _identifiedDeviceId;

    public string DeviceKey { get; }
    public string Model => "Attack Shark X68 PRO HE";
    public DeviceKind Kind => DeviceKind.Keyboard;
    public LinkKind Link { get; } = LinkKind.Wired;
    public ushort VendorId => X68Protocol.VendorId;
    public ushort ProductId { get; }

    /// <summary>Raised when the keyboard reports a change made on the keyboard itself (profile, LED, OS…).</summary>
    public event Action<byte[]>? VendorEvent;

    public X68Device(IReadOnlyList<HidDeviceInfo> collections)
    {
        var cfg = collections.First(c => c.UsagePage == ConfigUsagePage && c.Usage == ConfigUsage && c.FeatureReportLength > 0);
        ProductId = cfg.ProductId;
        DeviceKey = $"x68-{cfg.VendorId:x4}{cfg.ProductId:x4}{(string.IsNullOrWhiteSpace(cfg.Serial) ? "" : "-" + cfg.Serial)}".ToLowerInvariant();
        _io = new HidX68Transport(collections);
        _io.Event += OnEvent;
    }

    /// <summary>Device over any transport (the simulator for tests and the --demo mode).</summary>
    public X68Device(IX68Transport transport, string deviceKey, ushort productId = PidX68ProHeWired, LinkKind link = LinkKind.Wired)
    {
        ProductId = productId;
        Link = link;
        DeviceKey = deviceKey;
        _io = transport;
        _io.Event += OnEvent;
    }

    /// <summary>Delays between transfers; the simulator runs with zero.</summary>
    public int TransferDelayMs { get; init; } = 10;
    public int WriteSettleMs { get; init; } = 100;

    private void OnEvent(byte[] r)
    {
        if (r.Length < 4) return;
        if (r[1] == 0x0F) _deviceBusy = r[2] == 1;
        VendorEvent?.Invoke(r);
    }

    // ------------------------------------------------------------------ transport

    private void WaitIdle()
    {
        for (var i = 0; _deviceBusy && i < 50; i++) Thread.Sleep(20);
    }

    /// <summary>SET packets sent so far (used to tell whether a verification pass still had to write).</summary>
    private int _sets;

    private void Send(byte[] packet)
    {
        if (packet[0] >= 0xF0 || Array.IndexOf(Forbidden, packet[0]) >= 0)
            throw new InvalidOperationException($"Comando 0x{packet[0]:X2} bloqueado por segurança.");
        if (packet[0] < 0x80) _sets++;
        WaitIdle();
        if (TransferDelayMs > 0) Thread.Sleep(TransferDelayMs);
        _io.Send(packet);
    }

    private byte[] Receive()
    {
        if (TransferDelayMs > 0) Thread.Sleep(TransferDelayMs);
        return _io.Receive();
    }

    private byte[] Query(byte[] packet)
    {
        Send(packet);
        return Receive();
    }

    private byte[] Ask(params byte[] header) => Query(Command(header));

    private byte[] QueryEcho(byte cmd, int attempts = 3)
    {
        byte[] r = [];
        for (var i = 0; i < attempts; i++)
        {
            r = Ask(cmd);
            if (r[0] == cmd) return r;
            Thread.Sleep(30);
        }
        throw new IOException($"O teclado não respondeu ao comando 0x{cmd:X2}.");
    }

    private void VendorSleep(int ms = 100) { if (WriteSettleMs > 0) Thread.Sleep(ms * WriteSettleMs / 100); }

    private byte[] ReadPages(Func<int, byte[]> header, int pages)
    {
        var buf = new byte[pages * PacketLength];
        for (var p = 0; p < pages; p++) Query(Command(header(p))).CopyTo(buf, p * PacketLength);
        return buf;
    }

    private void WritePages(Func<int, int, bool, byte[]> header, ReadOnlySpan<byte> data, int pages, int? lastLength = null)
    {
        for (var p = 0; p < pages; p++)
        {
            var from = p * PageData;
            var count = Math.Max(0, Math.Min(PageData, data.Length - from));
            var len = p == pages - 1 && lastLength is { } l ? l : count;
            Send(Command(header(p, len, p == pages - 1), count > 0 ? data.Slice(from, count) : default));
        }
    }

    // ------------------------------------------------------------------ reading (backup)

    public X68Image ReadImage(IProgress<string>? progress = null, CancellationToken ct = default) => ReadImage(progress, ct, []);

    private X68Image ReadImage(IProgress<string>? progress, CancellationToken ct, IEnumerable<int> extraMacros)
    {
        var img = new X68Image();
        progress?.Report("identificação");
        _identifiedDeviceId = null;
        var id = QueryEcho(GetUsbVersion);
        img.DeviceId = BitConverter.ToUInt32(id, 1);
        _identifiedDeviceId = img.DeviceId;
        img.UsbFirmware = id[8] << 8 | id[7];
        var rf = Ask(GetRfVersion);
        img.RfFirmware = rf[0] == GetRfVersion ? rf[2] << 8 | rf[1] : 0;
        if (!X68Model.IsKnown(img.DeviceId))
            throw new NotSupportedException($"Teclado com id {img.DeviceId} não é um X68 PRO HE conhecido; nada foi alterado.");

        img.ActiveProfile = QueryEcho(GetProfile)[1];
        progress?.Report("iluminação e opções");
        img.Led = QueryEcho(GetLedParam);
        img.KbOption = QueryEcho(GetKbOption);
        img.Sleep = QueryEcho(GetSleepTime);
        img.Report = QueryEcho(GetReport);
        img.AutoOs = QueryEcho(GetAutoOs);
        img.Debounce = QueryEcho(GetDebounce);

        progress?.Report("camada Fn");
        img.FnWin = ReadPages(p => [GetFn, 0, 0, 0xFF, (byte)p], 8)[..MatrixBytes];
        img.FnMac = ReadPages(p => [GetFn, 1, 0, 0xFF, (byte)p], 8)[..MatrixBytes];

        progress?.Report("imagens de LED");
        for (var slot = 0; slot < img.UserPics.Length; slot++)
            img.UserPics[slot] = ReadPages(p => [GetUserPic, (byte)slot, 0xFF, (byte)p], 6);

        var original = img.ActiveProfile;
        var current = original;
        try
        {
            // Read the active profile first so a failure mid-way never leaves another profile selected for long.
            foreach (var profile in Enumerable.Range(0, Profiles).OrderBy(p => p == original ? 0 : 1))
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report($"perfil {profile + 1}: teclas");
                for (var son = 0; son < SonProfiles; son++)
                    img.Keys[profile][son] = ReadPages(p => [GetKeyMatrix, (byte)profile, 0xFF, (byte)p, (byte)son], 8)[..MatrixBytes];

                // Hall-effect tables only exist for the active profile: switch, read, switch back at the end
                // (the only writes of a backup — the same profile switch the official app does).
                progress?.Report($"perfil {profile + 1}: Hall Effect");
                if (profile != current)
                {
                    // The receiver and revision 2901 (also over cable) must never switch profiles.
                    if (!CanSwitchProfiles) continue;
                    SelectProfile(profile);
                    current = profile;
                }
                foreach (var op in ProfileOps)
                {
                    if (op == HeOp.TopDeadZone && !img.HasTopDeadZone) continue;
                    var size = op == HeOp.DksStages ? 512 : IsU16(op) ? 256 : 128;
                    img.HallEffect[profile][op] = ReadPages(p => [GetMultiMagnetism, (byte)op, 1, (byte)p], X68Protocol.ReadPages(op))[..size];
                }
            }
        }
        finally
        {
            if (CanSwitchProfiles && current != original) SelectProfile(original);
        }

        progress?.Report("macros");
        foreach (var idx in img.ReferencedMacros().Concat(extraMacros).Distinct())
        {
            var buf = new List<byte>();
            for (var page = 0; page < 4; page++)
            {
                var r = Ask(GetMacro, (byte)idx, (byte)page);
                buf.AddRange(r);
                if (ContainsTerminator(r)) break;
            }
            img.Macros[idx] = buf.Take(X68Macro.MaxBytes).ToArray();
        }
        if (!CanSwitchProfiles && QueryEcho(GetProfile)[1] != original)
            throw new IOException("O perfil ativo do teclado mudou durante a leitura. Leia o teclado novamente antes de aplicar alterações.");
        return img;
    }

    private static bool ContainsTerminator(byte[] page)
    {
        for (var i = 0; i + 3 < page.Length; i++)
            if (page[i] == 0 && page[i + 1] == 0 && page[i + 2] == 0 && page[i + 3] == 0) return true;
        return false;
    }

    // ------------------------------------------------------------------ writing

    public void WriteKeyMatrix(int profile, int son, byte[] matrix)
    {
        var pages = (MatrixBytes + PageData - 1) / PageData; // 10
        WritePages((p, len, last) => [SetKeyMatrix, (byte)profile, 0xFF, (byte)p, (byte)len, (byte)(last ? 1 : 0), (byte)son], matrix, pages);
        VendorSleep();
    }

    public void WriteFn(int os, byte[] matrix)
    {
        for (var p = 0; p < 10; p++)
        {
            var from = p * PageData;
            var count = Math.Min(PageData, MatrixBytes - from);
            Send(Command([SetFn, (byte)os, 0, 0xFF, (byte)p, (byte)(p == 9 ? 0 : PageData), (byte)(p == 9 ? 1 : 0)], matrix.AsSpan(from, count)));
        }
        VendorSleep();
    }

    public void WriteMacro(int index, byte[] raw)
    {
        var data = new byte[X68Macro.MaxBytes];
        raw.AsSpan(0, Math.Min(raw.Length, data.Length)).CopyTo(data);
        var pages = 0;
        for (var p = 0; p < 5; p++)
            if (data.AsSpan(p * PageData, Math.Min(PageData, data.Length - p * PageData)).IndexOfAnyExcept((byte)0) >= 0) pages = p + 1;
        pages = Math.Max(pages, 1);
        for (var p = 0; p < pages; p++)
        {
            var from = p * PageData;
            var count = Math.Min(PageData, data.Length - from);
            Send(Command([SetMacro, (byte)index, (byte)p, PageData, (byte)(p == pages - 1 ? 1 : 0)], data.AsSpan(from, count)));
        }
        VendorSleep();
    }

    public void WriteUserPic(int slot, byte[] rgb)
    {
        for (var p = 0; p < 7; p++)
        {
            var from = p * PageData;
            var count = Math.Min(PageData, Math.Max(0, 378 - from));
            Send(Command([SetUserPic, (byte)slot, 0xFF, (byte)p, (byte)(p == 6 ? 378 - PageData * 6 : PageData), (byte)(p == 6 ? 1 : 0)], rgb.AsSpan(from, count)));
        }
        VendorSleep();
    }

    public void WriteLed(byte[] led)
    {
        Send(Command([SetLedParam, led[1], led[2], led[3], led[4], led[5], led[6], led[7]], bit8: true));
        VendorSleep();
    }

    public void WriteKbOption(byte[] opt)
    {
        Send(Command([SetKbOption, 0, opt[2], opt[3], opt[4], opt[5]]));
        VendorSleep();
    }

    public void WriteSleep(byte[] sleep)
    {
        Send(Command([SetSleepTime], sleep.AsSpan(8, 8)));
        VendorSleep();
    }

    public void WritePolling(byte code) { Send(Command([SetReport, 0, code])); VendorSleep(); }
    public void WriteDebounce(byte value) { Send(Command([SetDebounce, value])); VendorSleep(); }
    public void WriteAutoOs(bool on) { Send(Command([SetAutoOs, (byte)(on ? 1 : 0)])); VendorSleep(); }

    /// <summary>
    /// Profile switching is allowed only after identifying the original revision (2370) over cable.
    /// Revision 2901 has also locked up after a wired profile switch. It and the receiver therefore use
    /// only the physical active profile's HE tables, without sending a profile switch even during backup.
    /// </summary>
    public bool CanSwitchProfiles => Link == LinkKind.Wired && _identifiedDeviceId == 2370;

    public void SelectProfile(int profile)
    {
        if (!CanSwitchProfiles)
            throw new InvalidOperationException("A troca de perfil pelo aplicativo está bloqueada para esta conexão ou revisão do teclado, pois pode interromper seu funcionamento. Troque o perfil no próprio teclado.");
        Send(Command([SetProfile, (byte)profile]));
        VendorSleep();
    }

    /// <summary>
    /// <see cref="WriteImage"/>, and over the 2.4 GHz receiver (where writes are not acknowledged) a read-back of
    /// the keyboard followed by a rewrite of whatever did not stick, until nothing differs.
    /// </summary>
    public void WriteImageVerified(X68Image img, X68Image? before, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // A sleeping keyboard would silently drop the writes: make sure it answers first.
        if (Link == LinkKind.Dongle) QueryEcho(GetUsbVersion);
        WriteImage(img, before, progress);
        if (Link != LinkKind.Dongle) return;
        for (var pass = 0; pass < 3; pass++)
        {
            var check = new Progress<string>(s => progress?.Report($"conferindo: {s}"));
            var now = ReadImage(check, ct, img.Macros.Keys);
            var sets = _sets;
            WriteImage(img, now, progress);
            if (_sets == sets) return;
        }
        throw new IOException("Algumas alterações não foram confirmadas pelo teclado via 2.4G. Tente aplicar de novo (ou use o cabo).");
    }

    /// <summary>Writes the Hall-effect tables of <paramref name="profile"/> that differ from <paramref name="before"/> (all when null).</summary>
    public void WriteHallEffect(X68Image img, int profile, X68Image? before)
    {
        bool Changed(HeOp op) => before is null || !img.He(profile, op).AsSpan().SequenceEqual(before.He(profile, op));
        var u16 = new[] { HeOp.Actuation, HeOp.Release, HeOp.RtPress, HeOp.RtRelease, HeOp.BottomDeadZone, HeOp.DksTravel }.Where(Changed).ToList();
        var u8 = new[] { HeOp.MtTime, HeOp.SnapPartner }.Where(Changed).ToList();
        if (img.HasTopDeadZone && Changed(HeOp.TopDeadZone)) u8.Add(HeOp.TopDeadZone);
        var modes = Changed(HeOp.KeyMode);
        var dks = Changed(HeOp.DksStages) || modes;
        var switches = Changed(HeOp.SwitchType);
        if (!modes && u16.Count == 0 && u8.Count == 0 && !dks && !switches) return;

        var dksSlots = Enumerable.Range(0, Slots).Where(s => img.GetKeyMode(profile, s) == KeyMode.Dks).ToList();
        var snapSlots = Enumerable.Range(0, Slots).Where(s => img.GetKeyMode(profile, s) == KeyMode.SnapTap).ToList();
        var bulks = new List<(HeOp Op, byte[] Data)>();
        if (modes) bulks.Add((HeOp.KeyMode, img.He(profile, HeOp.KeyMode)));
        bulks.AddRange(u16.Select(op => (op, img.He(profile, op))));
        bulks.AddRange(u8.Select(op => (op, img.He(profile, op))));
        var perKeyCount = (dks ? dksSlots.Count : 0) + (Changed(HeOp.SnapPartner) || modes ? snapSlots.Count : 0);

        for (var i = 0; i < bulks.Count; i++)
        {
            var (op, data) = bulks[i];
            var lastOfAll = i == bulks.Count - 1 && perKeyCount == 0;
            var pages = (data.Length + PageData - 1) / PageData;
            WritePages((p, _, last) => [SetMultiMagnetism, (byte)op, 1, (byte)p, (byte)(lastOfAll && last ? 1 : 0)], data, pages);
        }
        var sent = 0;
        if (dks)
            foreach (var slot in dksSlots)
            {
                byte[] stages = [img.DksStage(profile, slot, 0), img.DksStage(profile, slot, 1), img.DksStage(profile, slot, 2), img.DksStage(profile, slot, 3)];
                Send(Command([SetMultiMagnetism, (byte)HeOp.DksStageKey, 0, (byte)slot, (byte)(++sent == perKeyCount ? 1 : 0)], stages));
            }
        if (Changed(HeOp.SnapPartner) || modes)
            foreach (var slot in snapSlots)
                Send(Command([SetMultiMagnetism, (byte)HeOp.SnapPartner, 0, (byte)slot, (byte)(++sent == perKeyCount ? 1 : 0)], [img.He(profile, HeOp.SnapPartner)[slot]]));
        VendorSleep(500);
        if (switches)
        {
            var data = img.He(profile, HeOp.SwitchType);
            WritePages((p, _, last) => [SetMultiMagnetism, (byte)HeOp.SwitchType, 1, (byte)p, (byte)(last ? 1 : 0)], data, (data.Length + PageData - 1) / PageData);
        }
        VendorSleep();
    }

    /// <summary>Writes everything in <paramref name="img"/> that differs from <paramref name="before"/> (everything when null).</summary>
    public void WriteImage(X68Image img, X68Image? before, IProgress<string>? progress = null)
    {
        static bool Diff(byte[] a, byte[]? b) => b is null || !a.AsSpan().SequenceEqual(b);
        // HE writes implicitly address the physical active profile. A cached/editor profile number cannot
        // be trusted after an Fn switch on the keyboard (events may have been lost while the app was busy).
        var current = before?.ActiveProfile ?? -1;
        if (!CanSwitchProfiles)
        {
            current = QueryEcho(GetProfile)[1];
            if (current != (before?.ActiveProfile ?? img.ActiveProfile) || img.ActiveProfile != current)
                throw new IOException("O perfil ativo do teclado mudou. Leia o teclado novamente antes de aplicar alterações; nada foi gravado.");
        }
        var targetActive = CanSwitchProfiles ? img.ActiveProfile : current;
        for (var profile = 0; profile < Profiles; profile++)
        {
            for (var son = 0; son < SonProfiles; son++)
                if (Diff(img.Keys[profile][son], before?.Keys[profile][son]))
                {
                    progress?.Report($"perfil {profile + 1}: teclas");
                    WriteKeyMatrix(profile, son, img.Keys[profile][son]);
                }
            var heChanged = before is null || ProfileOps.Any(op => op is not (HeOp.DksStages or HeOp.LiveTravel)
                && img.HallEffect[profile].ContainsKey(op) && Diff(img.He(profile, op), before.HallEffect[profile].GetValueOrDefault(op)));
            heChanged |= before is not null && img.HallEffect[profile].ContainsKey(HeOp.DksStages)
                && Diff(img.He(profile, HeOp.DksStages), before.HallEffect[profile].GetValueOrDefault(HeOp.DksStages));
            // Without safe profile switching, HE writes must stay on the physical active profile.
            if (heChanged && (CanSwitchProfiles || (profile == current && HeWasRead(before ?? img, profile))))
            {
                progress?.Report($"perfil {profile + 1}: Hall Effect");
                if (current != profile) { SelectProfile(profile); current = profile; }
                WriteHallEffect(img, profile, before);
            }
        }
        if (current != targetActive) SelectProfile(targetActive);

        if (Diff(img.FnWin, before?.FnWin)) { progress?.Report("Fn (Windows)"); WriteFn(0, img.FnWin); }
        if (Diff(img.FnMac, before?.FnMac)) { progress?.Report("Fn (Mac)"); WriteFn(1, img.FnMac); }
        foreach (var (idx, data) in img.Macros)
            if (before is null || !before.Macros.TryGetValue(idx, out var old) || MacroDiffers(data, old)) { progress?.Report($"macro {idx + 1}"); WriteMacro(idx, data); }
        for (var i = 0; i < img.UserPics.Length; i++)
            if (Diff(img.UserPics[i], before?.UserPics[i])) { progress?.Report($"imagem LED {i + 1}"); WriteUserPic(i, img.UserPics[i]); }
        if (Diff(img.Led.AsSpan(1, 7).ToArray(), before?.Led.AsSpan(1, 7).ToArray())) { progress?.Report("iluminação"); WriteLed(img.Led); }
        if (Diff(img.KbOption.AsSpan(2, 4).ToArray(), before?.KbOption.AsSpan(2, 4).ToArray())) WriteKbOption(img.KbOption);
        if (Diff(img.Sleep.AsSpan(8, 8).ToArray(), before?.Sleep.AsSpan(8, 8).ToArray())) WriteSleep(img.Sleep);
        if (before is null || img.Report[2] != before.Report[2]) WritePolling(img.Report[2]);
        if (before is null || img.Debounce[1] != before.Debounce[1]) WriteDebounce(img.Debounce[1]);
        if (before is null || img.AutoOs[1] != before.AutoOs[1]) WriteAutoOs(img.AutoOsDetect);
    }

    private static bool MacroDiffers(byte[] desired, byte[] actual)
    {
        // GET returns complete 64-byte pages; the editor encodes only the events. Their zero padding
        // does not change the macro, and must not cause an endless write/read-back verification loop.
        for (var i = 0; i < Math.Max(desired.Length, actual.Length); i++)
            if ((i < desired.Length ? desired[i] : 0) != (i < actual.Length ? actual[i] : 0)) return true;
        return false;
    }

    /// <summary>Whether the Hall-effect tables of <paramref name="profile"/> came from the keyboard (not defaults).</summary>
    public static bool HeWasRead(X68Image img, int profile) => img.HallEffect[profile].ContainsKey(HeOp.Actuation);

    /// <summary>Min/zero calibration then max calibration, as the official "calibrate" button does.</summary>
    public void StartCalibration()
    {
        Send(Command([SetMagnetismCal, 1])); VendorSleep();
        Send(Command([SetMagnetismCal, 0])); VendorSleep();
        Send(Command([SetMagnetismMaxCal, 1])); VendorSleep();
    }

    public void StopCalibration() { Send(Command([SetMagnetismMaxCal, 0])); VendorSleep(); }

    /// <summary>Live travel of every key (only meaningful during calibration), in raw units.</summary>
    public int[] ReadLiveTravel()
    {
        var raw = ReadPages(p => [GetMultiMagnetism, (byte)HeOp.LiveTravel, 1, (byte)p], 4);
        return Enumerable.Range(0, Slots).Select(i => raw[i * 2] | raw[i * 2 + 1] << 8).ToArray();
    }

    public void FactoryReset()
    {
        Send(Command([SetReset]));
        VendorSleep(2000);
    }

    // ------------------------------------------------------------------ async + snapshot

    public async Task<T> RunAsync<T>(Func<T> work, CancellationToken ct = default)
    {
        await _busy.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(work, ct).ConfigureAwait(false); }
        finally { _busy.Release(); }
    }

    public Task RunAsync(Action work, CancellationToken ct = default) => RunAsync(() => { work(); return true; }, ct);

    public Task<DeviceSnapshot> ReadSnapshotAsync(IProgress<string>? progress = null, CancellationToken ct = default) =>
        RunAsync(() => ReadImage(progress, ct).ToSnapshot(DeviceKey, Model), ct);

    public Task RestoreSnapshotAsync(DeviceSnapshot snapshot, IProgress<string>? progress = null, CancellationToken ct = default) =>
        RunAsync(() => WriteImageVerified(X68Image.FromSnapshot(snapshot), null, progress, ct), ct);

    public void Dispose()
    {
        _io.Dispose();
        _busy.Dispose();
    }
}
