using System.Net;
using System.Net.Sockets;

namespace SharkBabyDrivers.Core.Lighting;

/// <summary>One colour sent by the SignalRGB plugin for one keyboard connection (product id).</summary>
public sealed record RgbFrame(ushort ProductId, byte R, byte G, byte B, bool Shutdown, long ReceivedAt);

/// <summary>
/// The colour of every key as SignalRGB draws it (matrix slot → RGB). The firmware cannot show this live; it is used
/// to pick the keyboard's own effect that looks most like the scene, and to store a static picture on request.
/// </summary>
public sealed record KeyColorsFrame(ushort ProductId, IReadOnlyDictionary<int, (byte R, byte G, byte B)> Keys, long ReceivedAt);

/// <summary>
/// Receives the colours of the SharkBabyDrivers plugin for SignalRGB (integrations/signalrgb). SignalRGB's HID
/// plugins can send UDP but not receive it, so the plugin never writes to the keyboard itself: it hands the colour
/// to SharkBabyDrivers on 127.0.0.1, which is the only program talking to the keyboard and decides whether the
/// colour is applied. Packet: "SBD1", version 1, type (1 = colour, 2 = shutdown colour), product id (BE), R, G, B.
/// Type 3 (per-key snapshot): "SBD1", 1, 3, product id (BE), count, then count × [slot, R, G, B].
/// </summary>
public sealed class SignalRgbBridge : IDisposable
{
    public const int Port = 47851;
    private static readonly byte[] Magic = "SBD1"u8.ToArray();

    private readonly object _lock = new();
    private readonly Dictionary<ushort, RgbFrame> _latest = new();
    private readonly Dictionary<ushort, KeyColorsFrame> _latestKeys = new();
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    public static SignalRgbBridge Shared { get; } = new();

    /// <summary>Why the bridge is not listening (port taken…), or null.</summary>
    public string? Error { get; private set; }
    public bool Listening => _udp is not null;
    public event Action<RgbFrame>? FrameReceived;
    /// <summary>Raised on the receive thread for every per-key snapshot.</summary>
    public event Action<KeyColorsFrame>? KeysReceived;

    public void Start()
    {
        if (_udp is not null) return;
        try
        {
            var udp = new UdpClient(AddressFamily.InterNetwork);
            udp.Client.ExclusiveAddressUse = true;
            udp.Client.Bind(new IPEndPoint(IPAddress.Loopback, Port));
            _udp = udp;
            Error = null;
        }
        catch (SocketException ex)
        {
            Error = $"Porta UDP {Port} indisponível ({ex.SocketErrorCode}).";
            return;
        }
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var udpClient = _udp;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var r = await udpClient.ReceiveAsync(token).ConfigureAwait(false);
                    if (!IPAddress.IsLoopback(r.RemoteEndPoint.Address)) continue;
                    var now = Environment.TickCount64;
                    if (Parse(r.Buffer, now) is { } frame) Accept(frame);
                    else if (ParseKeys(r.Buffer, now) is { } keys) AcceptKeys(keys);
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (SocketException) { /* ICMP "port unreachable" echoes and the like: keep listening */ }
            }
        }, token);
    }

    public static RgbFrame? Parse(ReadOnlySpan<byte> p, long now)
    {
        if (p.Length < 11 || !p[..4].SequenceEqual(Magic) || p[4] != 1 || p[5] is not (1 or 2)) return null;
        return new RgbFrame((ushort)(p[6] << 8 | p[7]), p[8], p[9], p[10], p[5] == 2, now);
    }

    public static KeyColorsFrame? ParseKeys(ReadOnlySpan<byte> p, long now)
    {
        if (p.Length < 9 || !p[..4].SequenceEqual(Magic) || p[4] != 1 || p[5] != 3) return null;
        var count = p[8];
        if (count == 0 || p.Length < 9 + count * 4) return null;
        var keys = new Dictionary<int, (byte, byte, byte)>(count);
        for (var i = 0; i < count; i++)
        {
            var o = 9 + i * 4;
            if (p[o] >= Devices.X68.X68Protocol.Slots) return null;
            keys[p[o]] = (p[o + 1], p[o + 2], p[o + 3]);
        }
        return new KeyColorsFrame((ushort)(p[6] << 8 | p[7]), keys, now);
    }

    public void AcceptKeys(KeyColorsFrame frame)
    {
        Interlocked.Increment(ref _keyFrames);
        lock (_lock) _latestKeys[frame.ProductId] = frame;
        KeysReceived?.Invoke(frame);
    }

    /// <summary>The newest per-key snapshot for this connection (or any connection), if recent enough.</summary>
    public KeyColorsFrame? LatestKeysFor(ushort productId, long now, long maxAgeMs = 3000)
    {
        lock (_lock)
        {
            if (_latestKeys.TryGetValue(productId, out var own) && now - own.ReceivedAt <= maxAgeMs) return own;
            return _latestKeys.Values.Where(f => now - f.ReceivedAt <= maxAgeMs).MaxBy(f => f.ReceivedAt);
        }
    }

    /// <summary>Per-key snapshots accepted since start (diagnostics).</summary>
    public long KeyFramesReceived => Interlocked.Read(ref _keyFrames);
    private long _keyFrames;

    /// <summary>Frames accepted since start (diagnostics).</summary>
    public long FramesReceived => Interlocked.Read(ref _frames);
    private long _frames;

    public void Accept(RgbFrame frame)
    {
        Interlocked.Increment(ref _frames);
        lock (_lock) _latest[frame.ProductId] = frame;
        FrameReceived?.Invoke(frame);
    }

    /// <summary>
    /// The colour to show on the keyboard connected as <paramref name="productId"/>: its own plugin instance when it
    /// has spoken in the last <paramref name="maxAgeMs"/>, otherwise the most recent of any instance (SignalRGB keeps
    /// one device per product id, and the user may have placed only one of them on the canvas).
    /// </summary>
    public RgbFrame? LatestFor(ushort productId, long now, long maxAgeMs = 3000)
    {
        lock (_lock)
        {
            if (_latest.TryGetValue(productId, out var own) && now - own.ReceivedAt <= maxAgeMs) return own;
            return _latest.Values.Where(f => now - f.ReceivedAt <= maxAgeMs).MaxBy(f => f.ReceivedAt);
        }
    }

    /// <summary>Milliseconds since the last frame from any plugin instance, or null when none ever arrived.</summary>
    public long? SilenceMs(long now)
    {
        lock (_lock) return _latest.Count == 0 ? null : now - _latest.Values.Max(f => f.ReceivedAt);
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _udp?.Dispose();
        _udp = null;
    }
}
