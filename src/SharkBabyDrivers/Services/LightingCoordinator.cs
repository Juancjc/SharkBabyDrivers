using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Devices.X68;
using SharkBabyDrivers.Core.Lighting;
using SharkBabyDrivers.Core.Storage;
using SharkBabyDrivers.Infrastructure;

namespace SharkBabyDrivers.Services;

/// <summary>
/// Decides who drives the X68's lighting and keeps the connected keyboard in line with it:
/// <list type="bullet">
/// <item><b>SignalRGB</b>: the keyboard is put in the host-driven screen-colour mode (one flash write), lighting
/// writes of the app are locked, and SignalRGB's colours (received by <see cref="SignalRgbBridge"/>) are streamed
/// with 0x0E between the app's own transfers.</item>
/// <item><b>SharkBabyDrivers</b>: colours from SignalRGB are ignored (nothing reaches the keyboard) and the lighting
/// the keyboard had before SignalRGB took over is written back.</item>
/// </list>
/// Nothing is written before the device has its ORIGINAL backup. Only one keyboard connection is driven at a time
/// (the cable wins over the receiver, see <see cref="X68Driver"/>).
/// </summary>
public sealed class LightingCoordinator : IDisposable
{
    private readonly AppSettings _settings;
    private readonly BackupStore _backups = new();
    private readonly SignalRgbBridge _bridge = SignalRgbBridge.Shared;
    private readonly Dictionary<X68Device, ScreenColorPump> _pumps = new();
    private X68Device? _current;
    private CancellationTokenSource? _applyCts;
    private string _state = "";

    public LightingCoordinator(AppSettings settings)
    {
        _settings = settings;
        if (!App.IsPreview) _bridge.Start();
    }

    /// <summary>Raised (on the UI thread) when the keyboard's lighting bytes were changed by the coordinator.</summary>
    public event Action<X68Device, byte[]>? LedChanged;
    /// <summary>Raised (on the UI thread) after a static picture was stored: device, picture slot, 378 RGB bytes.</summary>
    public event Action<X68Device, int, byte[]>? UserPicChanged;
    /// <summary>Raised (on the UI thread) whenever the owner, the connection or the apply state changes.</summary>
    public event Action? Changed;

    public bool SignalRgbOwns => _settings.KeyboardLightingOwner == LightingOwners.SignalRgb;
    public X68Device? Current => _current;
    public SignalRgbBridge Bridge => _bridge;
    /// <summary>Short description of what the coordinator is doing (for the tray tooltip / keyboard page).</summary>
    public string State => _state;
    public (byte R, byte G, byte B)? LastColor => _current is { } d && _pumps.TryGetValue(d, out var p) ? p.LastSent : null;

    /// <summary>True when the plugin spoke in the last 3 s.</summary>
    public bool Receiving => _bridge.SilenceMs(Environment.TickCount64) is < 3000;

    public void SetOwner(bool signalRgb)
    {
        var owner = signalRgb ? LightingOwners.SignalRgb : LightingOwners.App;
        if (_settings.KeyboardLightingOwner == owner) return;
        _settings.KeyboardLightingOwner = owner;
        _settings.Save();
        Restart();
    }

    /// <summary>A keyboard connection appeared (opened by the device manager).</summary>
    public void OnConnected(X68Device device)
    {
        if (device.DeviceKey.Contains("demo", StringComparison.Ordinal)) return;
        _pumps[device] = new ScreenColorPump(device, _bridge);
        _current = device;
        Restart();
    }

    public void OnDisconnected(X68Device device)
    {
        if (_pumps.Remove(device, out var pump)) pump.Dispose();
        if (_current == device)
        {
            _current = _pumps.Keys.LastOrDefault();
            Restart();
        }
    }

    /// <summary>A key of the X68 was pressed (Raw Input): lights back on if they went dark while idle.</summary>
    public void NotifyKeyActivity()
    {
        if (_current is { } d && _pumps.TryGetValue(d, out var p))
        {
            var wasDark = p.Dark;
            p.NotifyActivity();
            if (wasDark) { Trace("wake"); Ui.Post(() => Changed?.Invoke()); }
        }
    }

    /// <summary>The page wrote new sleep timers: the idle light-off follows them.</summary>
    public void OnSleepWritten(X68Device device, int idleSeconds)
    {
        if (_pumps.TryGetValue(device, out var p))
            p.IdleOffSeconds = device.Link == LinkKind.Dongle && idleSeconds is > 0 and <= X68Device.MaxSleepSeconds ? idleSeconds : 0;
    }

    public bool LightsDark => _current is { } d && _pumps.TryGetValue(d, out var p) && p.Active && p.Dark;

    /// <summary>The device page finished its first read (so the ORIGINAL backup now exists).</summary>
    public void OnDeviceReady(X68Device device)
    {
        if (device == _current) Restart();
    }

    private void Restart()
    {
        _applyCts?.Cancel();
        var cts = _applyCts = new CancellationTokenSource();
        if (_current is not { } device) { SetState(""); return; }
        _ = ApplyLoopAsync(device, cts.Token);
    }

    private async Task ApplyLoopAsync(X68Device device, CancellationToken ct)
    {
        var pump = _pumps[device];
        var signal = SignalRgbOwns;
        // Lock app lighting writes right away; the colours only start once the keyboard is in screen-colour mode.
        device.LightingLocked = signal;
        pump.Active = false;
        for (var attempt = 0; !ct.IsCancellationRequested; attempt++)
        {
            if (!_backups.HasOriginal(device.DeviceKey))
            {
                SetState("waiting_backup");
                return; // OnDeviceReady calls back once the page has read the keyboard and saved the ORIGINAL
            }
            try
            {
                SetState(signal ? "switching_to_signal" : "switching_to_app");
                var led = await device.RunAsync(() => Apply(device, signal), ct);
                if (ct.IsCancellationRequested) return;
                if (led is not null) Ui.Post(() => LedChanged?.Invoke(device, led));
                pump.Active = signal;
                SetState(signal ? "signal" : "app");
                return;
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (Exception ex)
            {
                if (ex is not DeviceAsleepException) App.Log(ex);
                SetState("waiting_keyboard");
                try { await Task.Delay(attempt < 3 ? 2000 : 5000, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }

    /// <summary>Runs inside the device queue. Returns the keyboard's new lighting bytes when they changed.</summary>
    private byte[]? Apply(X68Device device, bool signal)
    {
        // Light-off when idle (receiver only, like the keyboard's own timer): same value as "light off after".
        if (_pumps.TryGetValue(device, out var pump))
        {
            var sleep = device.ReadSleep();
            var idle = sleep[10] | sleep[11] << 8;
            pump.IdleOffSeconds = device.Link == LinkKind.Dongle && idle is > 0 and <= X68Device.MaxSleepSeconds ? idle : 0;
        }
        var led = device.ReadLed();
        var screen = led[1] == (byte)X68LightMode.ScreenColor;
        if (signal)
        {
            if (screen) return null;
            // Kept only when it is a plausible effect: a garbled answer must never be written back later.
            if (LedLooksValid(led)) _settings.KeyboardLedBeforeSignalRgb = Convert.ToHexString(led);
            _settings.Save();
            var start = _bridge.LatestFor(device.ProductId, Environment.TickCount64)
                        is { } f ? (f.R, f.G, f.B) : (led[5], led[6], led[7]);
            // Confirm the switch: over 2.4 GHz a lost write would leave the old effect on and every colour ignored.
            for (var attempt = 0; attempt < 3; attempt++)
            {
                device.EnterScreenColorMode(led, start.Item1, start.Item2, start.Item3);
                var after = device.ReadLed();
                if (after[1] == (byte)X68LightMode.ScreenColor) return after;
            }
            throw new IOException("O teclado não entrou no modo de cor ao vivo.");
        }
        if (!screen) return null;
        var restore = RestoreBytes(led);
        device.WriteLed(restore);
        return device.ReadLed();
    }

    /// <summary>What the keyboard showed before SignalRGB, or a steady light in the current colour.</summary>
    private byte[] RestoreBytes(byte[] current)
    {
        if (_settings.KeyboardLedBeforeSignalRgb is { } hex)
        {
            try
            {
                var saved = Convert.FromHexString(hex);
                if (LedLooksValid(saved)) return saved;
            }
            catch (FormatException) { }
        }
        var led = (byte[])current.Clone();
        led[1] = (byte)X68LightMode.AlwaysOn;
        led[2] = (byte)Math.Clamp((int)led[2], 0, X68Lighting.MaxSpeed);
        led[3] = (byte)Math.Clamp((int)led[3], 1, X68Lighting.MaxBrightness);
        led[4] = X68Lighting.CustomColor;
        return led;
    }

    /// <summary>
    /// Whether 0x87 bytes describe a real, non-host-driven effect within the firmware ranges (mode ≤ 25, speed and
    /// brightness ≤ 4, colour code ≤ 8). Over the receiver a garbled answer once read as 87 04 BF 04 FB FF FF FF.
    /// </summary>
    public static bool LedLooksValid(byte[] led) => X68Lighting.IsPlausibleEffect(led);

    /// <summary>True when SignalRGB sent the colour of each key in the last 3 s (needed for a static capture).</summary>
    public bool KeyColorsAvailable => _current is { } d && _bridge.LatestKeysFor(d.ProductId, Environment.TickCount64) is not null;

    /// <summary>
    /// Stores the colours SignalRGB currently draws on each key as LED picture <paramref name="slot"/> (0..4) and shows
    /// it (effect 13). One flash write, on request; the picture is static. The lighting goes back to the app (the
    /// live SignalRGB sync stops) without restoring the previous effect. Returns how many keys got a colour.
    /// </summary>
    public async Task<int> CaptureStaticPictureAsync(int slot)
    {
        if (_current is not { } device) throw new InvalidOperationException(Localization.Loc.T("capture_no_keyboard"));
        if (!_backups.HasOriginal(device.DeviceKey)) throw new InvalidOperationException(Localization.Loc.T("light_state_waiting_backup"));
        var frame = _bridge.LatestKeysFor(device.ProductId, Environment.TickCount64)
                    ?? throw new InvalidOperationException(Localization.Loc.T("capture_no_frame"));
        var rgb = new byte[X68Device.UserPicBytes];
        var count = 0;
        foreach (var (key, c) in frame.Keys)
        {
            if (key * 3 + 2 >= rgb.Length) continue;
            rgb[key * 3] = c.R; rgb[key * 3 + 1] = c.G; rgb[key * 3 + 2] = c.B;
            count++;
        }

        // Hand the lighting to the app first: no more streamed colours, lighting writes unlocked.
        _applyCts?.Cancel();
        _settings.KeyboardLightingOwner = LightingOwners.App;
        _settings.Save();
        if (_pumps.TryGetValue(device, out var pump)) pump.Active = false;
        device.LightingLocked = false;
        SetState("capturing");
        try
        {
            var led = await device.RunAsync(() =>
            {
                var current = device.ReadLed();
                device.WriteUserPicVerified(slot, rgb);
                device.EnterUserPictureMode(current, slot);
                return device.ReadLed();
            });
            Ui.Post(() =>
            {
                LedChanged?.Invoke(device, led);
                UserPicChanged?.Invoke(device, slot, rgb);
            });
            var top = frame.Keys.Values.GroupBy(c => c).OrderByDescending(g => g.Count()).Take(4)
                .Select(g => $"{g.Key.R:X2}{g.Key.G:X2}{g.Key.B:X2}x{g.Count()}");
            Trace($"capture slot={slot + 1} keys={count} colors={frame.Keys.Values.Distinct().Count()} top={string.Join(",", top)}");
            return count;
        }
        finally
        {
            SetState("app");
        }
    }

    /// <summary>One line for the keyboard page and the tray: who owns the lighting and what is happening.</summary>
    public string Describe()
    {
        if (_bridge.Error is { } err && SignalRgbOwns) return Localization.Loc.F("light_state_bridge_error", err);
        return _state switch
        {
            "" => Localization.Loc.T(SignalRgbOwns ? "light_state_signal_nokb" : "light_state_app_nokb"),
            "signal" when LightsDark => Localization.Loc.T("light_state_signal_dark"),
            "signal" => Localization.Loc.T(Receiving ? "light_state_signal_receiving" : "light_state_signal_silent"),
            _ => Localization.Loc.T("light_state_" + _state),
        };
    }

    /// <summary>The connection the lighting is being driven through, for the tray menu.</summary>
    public string ConnectionText => Localization.Loc.T(_current?.Link switch
    {
        LinkKind.Wired => "tray_kb_cable",
        LinkKind.Dongle => "tray_kb_dongle",
        _ => "tray_kb_none",
    });

    /// <summary>One line per state change in lighting.log (small, rotated): what a user can send when asking for help.</summary>
    public void Trace(string what)
    {
        try
        {
            var path = Path.Combine(AppPaths.Root, "lighting.log");
            var info = new FileInfo(path);
            if (info.Exists && info.Length > 200_000) info.Delete();
            var pump = _current is { } d && _pumps.TryGetValue(d, out var p) ? p : null;
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {what} owner={_settings.KeyboardLightingOwner} link={_current?.Link.ToString() ?? "-"} " +
                $"pid={_current?.ProductId:X4} state={_state} bridge={(_bridge.Listening ? "ok" : _bridge.Error)} frames={_bridge.FramesReceived} " +
                $"sent={pump?.ColorsSent ?? 0} active={pump?.Active ?? false} dark={pump?.Dark ?? false} idle={pump?.IdleOffSeconds ?? 0}s last={(LastColor is { } c ? $"{c.R:X2}{c.G:X2}{c.B:X2}" : "-")}" + Environment.NewLine);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private void SetState(string state)
    {
        if (_state != state) { _state = state; Trace("state"); }
        _state = state;
        Ui.Post(() => Changed?.Invoke());
    }

    public void Dispose()
    {
        _applyCts?.Cancel();
        foreach (var p in _pumps.Values) p.Dispose();
        _pumps.Clear();
        _bridge.Dispose();
    }
}
