using System.Runtime.Versioning;
using SharkBabyDrivers.Core.Hid;
using static SharkBabyDrivers.Core.Devices.X68.X68Protocol;

namespace SharkBabyDrivers.Core.Devices.X68;

/// <summary>Moves 64-byte packets to/from the keyboard (real HID or the simulator).</summary>
public interface IX68Transport : IDisposable
{
    void Send(byte[] packet);
    byte[] Receive();
    event Action<byte[]>? Event;
}

[SupportedOSPlatform("windows")]
internal sealed class HidX68Transport : IX68Transport
{
    private readonly HidConnection _config;
    private readonly HidConnection? _events;
    public event Action<byte[]>? Event;

    public HidX68Transport(IReadOnlyList<HidDeviceInfo> collections)
    {
        var cfg = collections.First(c => c.UsagePage == ConfigUsagePage && c.Usage == ConfigUsage && c.FeatureReportLength > 0);
        _config = HidConnection.Open(cfg);
        var ev = collections.FirstOrDefault(c => c.UsagePage == ConfigUsagePage && c.Usage == EventUsage && c.InputReportLength > 0);
        if (ev is null) return;
        try
        {
            _events = HidConnection.Open(ev, overlapped: true);
            _events.InputReport += r => Event?.Invoke(r);
            _events.StartInputLoop();
        }
        catch (HidException)
        {
            _events = null;
        }
    }

    public void Send(byte[] packet)
    {
        var frame = new byte[PacketLength + 1]; // report id 0 + 64 bytes
        packet.CopyTo(frame, 1);
        _config.SetFeature(frame);
    }

    public byte[] Receive() => _config.GetFeature(0x00, PacketLength + 1)[1..];

    public void Dispose()
    {
        _events?.Dispose();
        _config.Dispose();
    }
}

/// <summary>
/// The keyboard through its 2.4 GHz receiver (3151:5026). Same 64-byte packets as the cable, but the receiver
/// relays them over RF and keeps the keyboard's answer in a single cache slot:
/// <list type="number">
/// <item>the packet is sent as-is (report id 0) and forwarded to the keyboard;</item>
/// <item><c>F7</c> (receiver-local status) is polled until <c>has_response</c> (byte 0) is set;</item>
/// <item><c>FC</c> copies the cached answer into the feature buffer, which is then read.</item>
/// </list>
/// Writes have no answer: they are forwarded, then <c>F7</c> is polled until the RF link is idle again
/// (byte 5 = rf_ready) so pages of a bulk write never overtake each other; the keyboard then reports
/// busy/idle (<c>0F 01</c> / <c>0F 00</c>) on the event collection, which <see cref="X68Device"/> waits on.
/// Confirmed live on an X68 V2 (id 2901) with the 5026 receiver; matches the RY dongle firmware notes of
/// github.com/echtzeit-solutions/monsgeek-akko-linux. Another program driving the keyboard's lights
/// (SignalRGB does it ~25×/s) floods the link and makes most answers get lost.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class DongleX68Transport : IX68Transport
{
    private const byte DongleStatus = 0xF7, DongleFlush = 0xFC;
    private const int Attempts = 16;
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMilliseconds(700);

    private readonly HidConnection _config;
    private readonly HidConnection? _events;
    private byte[] _response = new byte[PacketLength];
    private readonly Queue<long> _busyEvents = new();
    private long _lastWrite;
    public event Action<byte[]>? Event;

    /// <summary>Last battery level reported by the receiver (0–100), or null before the first status.</summary>
    public int? Battery { get; private set; }

    public DongleX68Transport(IReadOnlyList<HidDeviceInfo> collections)
    {
        var cfg = collections.First(c => c.UsagePage == ConfigUsagePage && c.Usage == ConfigUsage && c.FeatureReportLength > 0);
        _config = HidConnection.Open(cfg);
        var ev = collections.FirstOrDefault(c => c.UsagePage == ConfigUsagePage && c.Usage == EventUsage && c.InputReportLength > 0);
        if (ev is null) return;
        try
        {
            _events = HidConnection.Open(ev, overlapped: true);
            _events.InputReport += OnEvent;
            _events.StartInputLoop();
        }
        catch (HidException)
        {
            _events = null;
        }
    }

    public void Send(byte[] packet)
    {
        var cmd = packet[0];
        // 0xF0–0xFF are handled by the receiver itself (pairing, boot loader…); the keyboard protocol never needs them.
        if (cmd >= 0xF0 || cmd is 0x7A or 0x7F || Array.IndexOf(Forbidden, cmd) >= 0)
            throw new InvalidOperationException($"Comando 0x{cmd:X2} não é enviado pelo receptor.");
        var frame = new byte[PacketLength + 1];
        packet.CopyTo(frame, 1);

        if (cmd < 0x80)
        {
            WaitIdle();
            Interlocked.Exchange(ref _lastWrite, Environment.TickCount64);
            _config.SetFeature(frame);
            WaitIdle();
            _response = new byte[PacketLength];
            return;
        }

        // The answer sits in the receiver's single cache slot only briefly, so the status is polled back-to-back.
        // Answers can get lost, come back as zeros or (right after the receiver is opened) as leftovers of an
        // older exchange, so one is accepted only once it was seen identically twice — three times when it is
        // all zeros. The cost is one extra round trip (~20 ms) per read.
        var seen = new Dictionary<string, int>();
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            WaitIdle();
            _config.SetFeature(frame);
            var answer = Await();
            if (answer is null) continue;
            var hex = Convert.ToHexString(answer);
            var count = seen[hex] = seen.GetValueOrDefault(hex) + 1;
            if (count >= (answer.AsSpan().IndexOfAnyExcept((byte)0) < 0 ? 3 : 2)) { _response = answer; return; }
        }
        if (ForeignTraffic)
            throw new IOException("Outro programa está controlando as luzes do teclado pelo receptor (ex.: SignalRGB) e as respostas se perdem. Feche-o ou desative o teclado nele e tente de novo.");
        throw new DeviceAsleepException($"O teclado não respondeu pelo receptor (comando 0x{cmd:X2}). Aperte uma tecla para acordá-lo (ele precisa estar ligado no modo 2.4G).");
    }

    public byte[] Receive() => _response;

    private void OnEvent(byte[] r)
    {
        if (r.Length > 1 && r[1] == 0x0F)
        {
            var now = Environment.TickCount64;
            lock (_busyEvents)
            {
                // Only writes make the keyboard report busy/idle: reports with no write of ours nearby mean
                // someone else is writing to it.
                if (now - Interlocked.Read(ref _lastWrite) > 1000) _busyEvents.Enqueue(now);
                while (_busyEvents.Count > 0 && now - _busyEvents.Peek() > 3000) _busyEvents.Dequeue();
            }
        }
        Event?.Invoke(r);
    }

    /// <summary>True when another program keeps writing to the keyboard (busy reports nobody here asked for).</summary>
    public bool ForeignTraffic
    {
        get { lock (_busyEvents) return _busyEvents.Count > 10 && Environment.TickCount64 - _busyEvents.Last() < 3000; }
    }

    private byte[]? Await()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < AnswerTimeout)
        {
            if (Status()[0] == 0) continue;
            _config.SetFeature(Local(DongleFlush));
            return _config.GetFeature(0x00, PacketLength + 1)[1..];
        }
        return null;
    }

    private byte[] Status()
    {
        _config.SetFeature(Local(DongleStatus));
        var s = _config.GetFeature(0x00, PacketLength + 1)[1..];
        if (s[1] <= 100) Battery = s[1];
        return s;
    }

    /// <summary>Waits until the receiver is not waiting on the keyboard (rf_ready).</summary>
    private void WaitIdle()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.Elapsed < IdleTimeout && Status()[5] == 0) Thread.Sleep(2);
    }

    private static byte[] Local(byte cmd)
    {
        var f = new byte[PacketLength + 1];
        f[1] = cmd;
        f[8] = (byte)(0xFF - cmd);
        return f;
    }

    public void Dispose()
    {
        _events?.Dispose();
        _config.Dispose();
    }
}

/// <summary>
/// In-memory model of the RY5088 firmware used by tests and the --demo mode. It answers GET commands from an
/// <see cref="X68Image"/>, applies SET commands to it, and rejects any packet with a wrong checksum or layout —
/// so it doubles as a strict validator of what the app would send to a real keyboard.
/// </summary>
public sealed class X68Simulator : IX68Transport
{
    private byte[] _response = new byte[PacketLength];
    private readonly Dictionary<(byte Cmd, int A, int B), byte[]> _bulk = new();
    public X68Image State { get; }
    /// <summary>Last colour received through SetScreenColor (0x0E).</summary>
    public (byte R, byte G, byte B)? ScreenColor { get; private set; }
    public List<byte[]> Sent { get; } = new();
    public event Action<byte[]>? Event;

    public X68Simulator(X68Image state) => State = state;

    public void Send(byte[] p)
    {
        if (p.Length != PacketLength) throw new InvalidDataException($"pacote com {p.Length} bytes");
        Sent.Add((byte[])p.Clone());
        var cmd = p[0];
        if (cmd == SetLedParam)
        {
            if (p[8] != (byte)(0xFF - (p.Take(8).Sum(b => b) & 0xFF))) throw new InvalidDataException("checksum Bit8 inválido (LED)");
        }
        else if (p[7] != (byte)(0xFF - (p.Take(7).Sum(b => b) & 0xFF)))
            throw new InvalidDataException($"checksum Bit7 inválido no comando 0x{cmd:X2}");
        if (Array.IndexOf(Forbidden, cmd) >= 0) throw new InvalidOperationException($"comando proibido 0x{cmd:X2}");

        var r = new byte[PacketLength];
        var payload = p.AsSpan(8);
        var s = State;
        switch (cmd)
        {
            case GetUsbVersion:
                r[0] = cmd;
                BitConverter.GetBytes(s.DeviceId).CopyTo(r, 1);
                r[7] = (byte)s.UsbFirmware; r[8] = (byte)(s.UsbFirmware >> 8);
                break;
            case GetRfVersion: r[0] = cmd; r[1] = (byte)s.RfFirmware; r[2] = (byte)(s.RfFirmware >> 8); break;
            case GetProfile: r[0] = cmd; r[1] = (byte)s.ActiveProfile; break;
            case GetLedParam: r = Echo(s.Led, cmd); break;
            case GetKbOption: r = Echo(s.KbOption, cmd); break;
            case GetSleepTime: r = Echo(s.Sleep, cmd); break;
            case GetReport: r = Echo(s.Report, cmd); break;
            case GetAutoOs: r = Echo(s.AutoOs, cmd); break;
            case GetDebounce: r = Echo(s.Debounce, cmd); break;
            case GetFn: r = Page(p[1] == 0 ? s.FnWin : s.FnMac, p[4]); break;
            case GetUserPic: r = Page(s.UserPics[p[1]], p[3]); break;
            case GetKeyMatrix: r = Page(s.Keys[p[1]][p[4]], p[3]); break;
            case GetMultiMagnetism: r = Page(s.He(s.ActiveProfile, (HeOp)p[1]), p[3]); break;
            case GetMacro: r = Page(s.Macros.GetValueOrDefault(p[1]) ?? new byte[X68Macro.MaxBytes], p[2]); break;

            case SetProfile: s.ActiveProfile = p[1]; break;
            case SetLedParam: Array.Copy(p, 1, s.Led, 1, 7); break;
            case SetKbOption: Array.Copy(p, 2, s.KbOption, 2, 4); break;
            case SetSleepTime: Array.Copy(p, 8, s.Sleep, 8, 8); break;
            case SetReport: s.Report[2] = p[2]; break;
            case SetDebounce: s.Debounce[1] = p[1]; break;
            case SetAutoOs: s.AutoOs[1] = p[1]; break;
            case SetKeyMatrix:
                if (p[2] != 0xFF) { payload[..4].CopyTo(s.Keys[p[1]][p[6]].AsSpan(p[2] * 4)); break; }
                Collect(cmd, p[1], p[6], p[3], payload[..p[4]], p[5] == 1, MatrixBytes, d => s.Keys[p[1]][p[6]] = d);
                break;
            case SetFn:
                Collect(cmd, p[1], 0, p[4], payload[..(p[4] == 9 ? 8 : p[5])], p[6] == 1, MatrixBytes, d => { if (p[1] == 0) s.FnWin = d; else s.FnMac = d; });
                break;
            case SetUserPic:
                Collect(cmd, p[1], 0, p[3], payload[..p[4]], p[5] == 1, 378, d => d.CopyTo(s.UserPics[p[1]], 0));
                break;
            case SetMacro:
                Collect(cmd, p[1], 0, p[2], payload[..p[3]], p[4] == 1, -1, d => s.Macros[p[1]] = d.Take(X68Macro.MaxBytes).ToArray());
                break;
            case SetMultiMagnetism:
            {
                var op = (HeOp)p[1];
                if (p[2] == 0)
                {
                    var slot = p[3];
                    if (op == HeOp.DksStageKey) for (var t = 0; t < 4; t++) s.SetDksStage(s.ActiveProfile, slot, t, payload[t]);
                    else if (IsU16(op)) s.SetHeValue(s.ActiveProfile, op, slot, payload[0] | payload[1] << 8);
                    else s.He(s.ActiveProfile, op)[slot] = payload[0];
                    break;
                }
                var size = IsU16(op) ? 256 : 128;
                var profile = s.ActiveProfile;
                Collect(cmd, (int)op, 0, p[3], payload, (p[3] + 1) * PageData >= size, size, d => d.CopyTo(s.He(profile, op), 0));
                break;
            }
            case SetScreenColor: ScreenColor = (p[1], p[2], p[3]); break;
            case SetMagnetismCal or SetMagnetismMaxCal or SetMagnetismReport or SetReset: break;
            default: throw new InvalidDataException($"comando desconhecido 0x{cmd:X2}");
        }
        _response = r;
    }

    private static byte[] Echo(byte[] stored, byte cmd)
    {
        var r = (byte[])stored.Clone();
        r[0] = cmd;
        return r;
    }

    private static byte[] Page(byte[] data, int page)
    {
        var r = new byte[PacketLength];
        var from = page * PacketLength;
        if (from < data.Length) data.AsSpan(from, Math.Min(PacketLength, data.Length - from)).CopyTo(r);
        return r;
    }

    /// <summary>Accumulates a multi-page write; pages must arrive in order starting at 0.</summary>
    private void Collect(byte cmd, int a, int b, int page, ReadOnlySpan<byte> data, bool last, int size, Action<byte[]> commit)
    {
        var key = (cmd, a, b);
        if (page == 0) _bulk[key] = [];
        if (!_bulk.TryGetValue(key, out var acc) || acc.Length != page * PageData)
            throw new InvalidDataException($"página {page} fora de ordem no comando 0x{cmd:X2}");
        var grown = new byte[acc.Length + data.Length];
        acc.CopyTo(grown, 0);
        data.CopyTo(grown.AsSpan(acc.Length));
        _bulk[key] = grown;
        if (!last && (size < 0 || grown.Length < size)) return;
        var result = size > 0 ? grown.AsSpan(0, Math.Min(size, grown.Length)).ToArray() : grown;
        if (size > 0 && result.Length < size) Array.Resize(ref result, size);
        commit(result);
        _bulk.Remove(key);
    }

    public byte[] Receive() => _response;

    public void RaiseEvent(byte[] e) => Event?.Invoke(e);

    public void Dispose() { }

    /// <summary>A plausible factory-state keyboard for demos and tests.</summary>
    public static X68Image FactoryImage(uint id = 2370, int fw = 0x0410)
    {
        var model = X68Model.ForId(id);
        var img = new X68Image { DeviceId = id, UsbFirmware = fw };
        for (var p = 0; p < Profiles; p++)
        {
            img.Keys[p][0] = (byte[])model.DefaultKeys.Clone();
            foreach (var op in ProfileOps) img.He(p, op);
        }
        img.FnWin = (byte[])model.DefaultFnWin.Clone();
        img.FnMac = (byte[])model.DefaultFnMac.Clone();
        img.Led[1] = (byte)X68LightMode.Wave; img.Led[2] = 2; img.Led[3] = 3; img.Led[4] = X68Lighting.Rainbow;
        img.Report[2] = 0;
        img.Debounce[1] = 2;
        X68Image.PutU16(img.Sleep, 8, 120); X68Image.PutU16(img.Sleep, 10, 120);
        X68Image.PutU16(img.Sleep, 12, 1680); X68Image.PutU16(img.Sleep, 14, 1680);
        return img;
    }
}
