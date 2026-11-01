using System.Runtime.Versioning;
using SharkBabyDrivers.Core.Devices.X68;

namespace SharkBabyDrivers.Core.Lighting;

/// <summary>
/// Moves SignalRGB's colours to one connected keyboard. Each colour goes through <see cref="X68Device.RunAsync"/>,
/// the same queue as every read and write of the app, so a backup, an Apply or a profile load simply holds the
/// colours back for its duration; afterwards only the newest colour is sent (nothing piles up). Colours are sent
/// when they change, plus a refresh every <see cref="RefreshMs"/> (a keyboard that woke up gets the colour back).
/// <para>
/// Light-off when idle: the keyboard's own "light off after" timer does not fire while colours keep arriving, so
/// after <see cref="IdleOffSeconds"/> without a key press on this keyboard the pump sends black once and goes quiet
/// (no RF traffic, so the keyboard's own sleep timers run as usual); the next key press brings the colours back.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ScreenColorPump : IDisposable
{
    public const int FrameMs = 33;       // at most ~30 colours per second
    public const int RefreshMs = 2000;

    private readonly X68Device _device;
    private readonly SignalRgbBridge _bridge;
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _active;
    private (byte R, byte G, byte B)? _sent;
    private long _sentAt;
    private long _lastActivity = Environment.TickCount64;
    private volatile bool _dark;

    public ScreenColorPump(X68Device device, SignalRgbBridge bridge)
    {
        _device = device;
        _bridge = bridge;
        _ = Task.Run(LoopAsync);
    }

    /// <summary>True while SignalRGB owns this keyboard's lighting (and the keyboard is in screen-colour mode).</summary>
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            if (!value) return;
            _sent = null;
            _dark = false;
            Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);
        }
    }

    /// <summary>Seconds without a key press before the lights go dark; 0 = never (cable, or the user's choice).</summary>
    public int IdleOffSeconds { get; set; }

    /// <summary>True while the lights are off because nobody typed for <see cref="IdleOffSeconds"/>.</summary>
    public bool Dark => _dark;

    /// <summary>A key of this keyboard was pressed: lights back on right away.</summary>
    public void NotifyActivity()
    {
        Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);
        if (!_dark) return;
        _dark = false;
        _sent = null; // force the current colour out on the next frame
    }

    /// <summary>Colours sent to the keyboard since start (diagnostics).</summary>
    public long ColorsSent => Interlocked.Read(ref _colorsSent);
    private long _colorsSent;

    /// <summary>Last colour actually sent to the keyboard.</summary>
    public (byte R, byte G, byte B)? LastSent => _sent;
    public long LastSentAt => Interlocked.Read(ref _sentAt);

    private async Task LoopAsync()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(FrameMs, token).ConfigureAwait(false);
                if (!_active) continue;
                var now = Environment.TickCount64;
                if (_dark) continue;
                if (IdleOffSeconds > 0 && now - Interlocked.Read(ref _lastActivity) >= IdleOffSeconds * 1000L)
                {
                    await _device.RunAsync(() => { if (_active) _device.SendScreenColor(0, 0, 0); }, token).ConfigureAwait(false);
                    _dark = true;
                    _sent = (0, 0, 0);
                    continue;
                }
                if (_bridge.LatestFor(_device.ProductId, now) is not { } f) continue;
                var color = (f.R, f.G, f.B);
                if (_sent == color && now - LastSentAt < RefreshMs) continue;
                await _device.RunAsync(() =>
                {
                    // Re-checked inside the queue: the owner may have changed while a transaction was running.
                    if (_active) _device.SendScreenColor(color.R, color.G, color.B);
                }, token).ConfigureAwait(false);
                _sent = color;
                Interlocked.Increment(ref _colorsSent);
                Interlocked.Exchange(ref _sentAt, Environment.TickCount64);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (Exception)
            {
                // Keyboard asleep / unplugged mid-frame: try again a bit later.
                try { await Task.Delay(1000, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
        }
    }

    public void Dispose()
    {
        _active = false;
        _cts.Cancel();
    }
}
