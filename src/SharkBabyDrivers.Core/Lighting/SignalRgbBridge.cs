using System.Net;
using System.Net.Sockets;

namespace SharkBabyDrivers.Core.Lighting;

/// <summary>One colour sent by the SignalRGB plugin for one keyboard connection (product id).</summary>
public sealed record RgbFrame(ushort ProductId, byte R, byte G, byte B, bool Shutdown, long ReceivedAt);

/// <summary>
/// Receives the colours of the SharkBabyDrivers plugin for SignalRGB (integrations/signalrgb). SignalRGB's HID
/// plugins can send UDP but not receive it, so the plugin never writes to the keyboard itself: it hands the colour
/// to SharkBabyDrivers on 127.0.0.1, which is the only program talking to the keyboard and decides whether the
/// colour is applied. Packet: "SBD1", version 1, type (1 = colour, 2 = shutdown colour), product id (BE), R, G, B.
/// </summary>
public sealed class SignalRgbBridge : IDisposable
{
    public const int Port = 47851;
    private static readonly byte[] Magic = "SBD1"u8.ToArray();

    private readonly object _lock = new();
    private readonly Dictionary<ushort, RgbFrame> _latest = new();
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    public static SignalRgbBridge Shared { get; } = new();

    /// <summary>Why the bridge is not listening (port taken…), or null.</summary>
    public string? Error { get; private set; }
    public bool Listening => _udp is not null;
    public event Action<RgbFrame>? FrameReceived;

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
                    if (Parse(r.Buffer, Environment.TickCount64) is { } frame) Accept(frame);
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
