using SharkBabyDrivers.Core.Devices.X68;
using SharkBabyDrivers.Core.Lighting;
using static SharkBabyDrivers.Core.Devices.X68.X68Protocol;

namespace SharkBabyDrivers.Tests;

/// <summary>SignalRGB integration: the app is the only writer and leaves the lighting alone while SignalRGB owns it.</summary>
public class SignalRgbLightingTests
{
    private static (X68Simulator Sim, X68Device Dev) Create()
    {
        var sim = new X68Simulator(X68Simulator.FactoryImage(2370, 0x0410));
        return (sim, new X68Device(sim, "sim") { TransferDelayMs = 0, WriteSettleMs = 0 });
    }

    [Fact]
    public void Locked_lighting_is_never_written_by_apply_profile_or_restore()
    {
        var (sim, dev) = Create();
        var before = dev.ReadImage();
        var edited = before.Clone();
        edited.LightMode = X68LightMode.Breath;
        edited.UserPics[0][0] = 0x42;
        edited.Debounce[1] = 5;

        dev.LightingLocked = true;
        sim.Sent.Clear();
        dev.WriteImage(edited, before);
        Assert.DoesNotContain(sim.Sent, p => p[0] is SetLedParam or SetUserPic);
        Assert.Contains(sim.Sent, p => p[0] == SetDebounce); // everything else still goes through

        sim.Sent.Clear();
        dev.WriteImage(edited, null); // restoring a backup writes "everything"
        Assert.DoesNotContain(sim.Sent, p => p[0] is SetLedParam or SetUserPic);
    }

    [Fact]
    public void Screen_colour_mode_is_never_written_from_a_profile_or_backup()
    {
        var (sim, dev) = Create();
        var before = dev.ReadImage();
        var fromProfile = before.Clone();
        fromProfile.Led[1] = (byte)X68LightMode.ScreenColor; // taken while SignalRGB owned the lights
        sim.Sent.Clear();
        dev.WriteImage(fromProfile, before);
        Assert.DoesNotContain(sim.Sent, p => p[0] == SetLedParam);
    }

    [Fact]
    public void Entering_screen_colour_mode_then_streaming_uses_0x0E_without_flash_writes()
    {
        var (sim, dev) = Create();
        var led = dev.ReadLed();
        dev.EnterScreenColorMode(led, 0x10, 0x20, 0x30);
        Assert.Equal((byte)X68LightMode.ScreenColor, sim.State.Led[1]);
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30 }, sim.State.Led[5..8]);

        sim.Sent.Clear();
        dev.SendScreenColor(0xFF, 0x00, 0x7F);
        var packet = Assert.Single(sim.Sent);
        Assert.Equal(SetScreenColor, packet[0]);
        Assert.Equal((byte)(0xFF - ((0x0E + 0xFF + 0x7F) & 0xFF)), packet[7]); // Bit7 checksum (also checked by the simulator)
        Assert.Equal(((byte)0xFF, (byte)0x00, (byte)0x7F), sim.ScreenColor);
        Assert.Equal((byte)X68LightMode.ScreenColor, sim.State.Led[1]); // the stored effect did not change
    }

    [Fact]
    public void Bridge_parses_plugin_packets_and_rejects_anything_else()
    {
        byte[] good = [0x53, 0x42, 0x44, 0x31, 1, 1, 0x50, 0x26, 9, 8, 7];
        var f = SignalRgbBridge.Parse(good, 100);
        Assert.NotNull(f);
        Assert.Equal((ushort)0x5026, f!.ProductId);
        Assert.Equal((9, 8, 7), (f.R, f.G, f.B));
        Assert.False(f.Shutdown);

        Assert.Null(SignalRgbBridge.Parse(good[..10], 0));
        Assert.Null(SignalRgbBridge.Parse([0x53, 0x42, 0x44, 0x32, 1, 1, 0x50, 0x26, 9, 8, 7], 0));
        Assert.Null(SignalRgbBridge.Parse([0x53, 0x42, 0x44, 0x31, 1, 9, 0x50, 0x26, 9, 8, 7], 0));
    }

    [Fact]
    public void Bridge_prefers_the_frames_of_the_connection_in_use()
    {
        var bridge = new SignalRgbBridge();
        bridge.Accept(new RgbFrame(0x5026, 1, 1, 1, false, 1000));
        bridge.Accept(new RgbFrame(0x502F, 2, 2, 2, false, 1500));
        Assert.Equal(1, bridge.LatestFor(0x5026, 2000)!.R);
        Assert.Equal(2, bridge.LatestFor(0x502F, 2000)!.R);
        // The receiver's own instance went quiet: fall back to the newest one.
        Assert.Equal(2, bridge.LatestFor(0x5026, 4200)!.R);
        Assert.Null(bridge.LatestFor(0x5026, 9000));
    }

    [Fact]
    public async Task Idle_light_off_goes_dark_once_and_a_key_press_brings_the_colour_back()
    {
        var (sim, dev) = Create();
        var bridge = new SignalRgbBridge();
        using var pump = new ScreenColorPump(dev, bridge) { IdleOffSeconds = 1 };
        pump.Active = true;
        var feeding = true;
        var feeder = Task.Run(async () =>
        {
            while (feeding) { bridge.Accept(new RgbFrame(dev.ProductId, 0x10, 0x80, 0x20, false, Environment.TickCount64)); await Task.Delay(30); }
        });

        await WaitFor(() => sim.ScreenColor == (0x10, 0x80, 0x20));
        await WaitFor(() => pump.Dark, 8000);
        Assert.Equal(((byte)0, (byte)0, (byte)0), sim.ScreenColor);
        var sent = sim.Sent.Count;
        await Task.Delay(400);
        Assert.Equal(sent, sim.Sent.Count); // quiet while dark: nothing goes over the air

        pump.NotifyActivity();
        await WaitFor(() => sim.ScreenColor == (0x10, 0x80, 0x20));
        Assert.False(pump.Dark);
        feeding = false;
        await feeder;
    }

    [Fact]
    public void Out_of_range_sleep_timers_from_a_bad_backup_are_never_written()
    {
        var (sim, dev) = Create();
        var before = dev.ReadImage();
        var bad = before.Clone();
        X68Image.PutU16(bad.Sleep, 14, 65108); // what the corrupted cable backup holds
        bad.Debounce[1] = 6;
        sim.Sent.Clear();
        dev.WriteImage(bad, before);
        Assert.DoesNotContain(sim.Sent, p => p[0] == SetSleepTime);
        Assert.Contains(sim.Sent, p => p[0] == SetDebounce);
        Assert.Throws<InvalidDataException>(() => dev.WriteSleep(bad.Sleep));
    }

    private static async Task WaitFor(Func<bool> condition, int timeoutMs = 6000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs) Assert.Fail("condição não atingida a tempo");
            await Task.Delay(20);
        }
    }

    [Fact]
    public void Plugin_never_writes_to_the_keyboard_and_covers_cable_and_receiver()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "SharkBabyDrivers.sln"))) root = Path.GetDirectoryName(root)!;
        var js = File.ReadAllText(Path.Combine(root, "integrations", "signalrgb", "Attack_Shark_X68_Pro_HE_SharkBabyDrivers.js"));
        Assert.DoesNotContain("send_report", js);
        Assert.DoesNotContain("device.write", js);
        Assert.Contains("0x502F", js);
        Assert.Contains("0x5026", js);
        Assert.Contains("udp.send", js);
    }
}

/// <summary>Static multi-colour picture captured from SignalRGB (the firmware has no per-key live colour).</summary>
public class SignalRgbStaticPictureTests
{
    private static (X68Simulator Sim, X68Device Dev) Create()
    {
        var sim = new X68Simulator(X68Simulator.FactoryImage(2901, 0x0505));
        return (sim, new X68Device(sim, "sim") { TransferDelayMs = 0, WriteSettleMs = 0 });
    }

    [Fact]
    public void Key_map_covers_every_key_of_the_v2_layout_once_inside_the_grid()
    {
        var keys = SignalRgbKeyMap.Keys;
        Assert.Equal(X68Model.Rev2901.Caps.Count, keys.Count);
        Assert.Equal(keys.Count, keys.Select(k => k.Slot).Distinct().Count());
        Assert.Equal(keys.Count, keys.Select(k => k.Name).Distinct().Count());
        Assert.All(keys, k => Assert.InRange(k.X, 0, SignalRgbKeyMap.GridWidth - 1));
        Assert.All(keys, k => Assert.InRange(k.Y, 0, SignalRgbKeyMap.GridHeight - 1));
        Assert.All(keys, k => Assert.True(k.Name.All(c => c < 128), k.Name));
        // keys of the same row never share a cell
        Assert.Equal(keys.Count, keys.Select(k => (k.X, k.Y)).Distinct().Count());
    }

    [Fact]
    public void Plugin_declares_exactly_the_app_key_map()
    {
        var root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "SharkBabyDrivers.sln"))) root = Path.GetDirectoryName(root)!;
        var js = File.ReadAllText(Path.Combine(root, "integrations", "signalrgb", "Attack_Shark_X68_Pro_HE_SharkBabyDrivers.js")).Replace("\r\n", "\n");
        Assert.Contains(SignalRgbKeyMap.ToJavaScript(), js);
        Assert.Contains("export function Size() { return [16, 6]; }", js);
        Assert.DoesNotContain("send_report", js);
        Assert.True(js.All(c => c < 128), "plugin must stay ASCII");
    }

    [Fact]
    public void Bridge_parses_per_key_snapshots_and_rejects_bad_ones()
    {
        byte[] good = [0x53, 0x42, 0x44, 0x31, 1, 3, 0x50, 0x2F, 2, 6, 255, 0, 0, 41, 0, 0, 255];
        var f = SignalRgbBridge.ParseKeys(good, 10);
        Assert.NotNull(f);
        Assert.Equal((ushort)0x502F, f!.ProductId);
        Assert.Equal(((byte)255, (byte)0, (byte)0), f.Keys[6]);
        Assert.Equal(((byte)0, (byte)0, (byte)255), f.Keys[41]);
        Assert.Null(SignalRgbBridge.Parse(good, 10));                                  // not a colour packet
        Assert.Null(SignalRgbBridge.ParseKeys(good[..15], 10));                        // truncated
        byte[] badSlot = [0x53, 0x42, 0x44, 0x31, 1, 3, 0x50, 0x2F, 1, 200, 1, 2, 3];
        Assert.Null(SignalRgbBridge.ParseKeys(badSlot, 10));                           // slot outside the matrix

        var bridge = new SignalRgbBridge();
        bridge.AcceptKeys(f);
        Assert.Same(f, bridge.LatestKeysFor(0x5026, 1000));                            // newest of any connection
        Assert.Null(bridge.LatestKeysFor(0x502F, 9000));                               // too old
    }

    [Fact]
    public void Picture_is_written_once_verified_and_shown_with_effect_13()
    {
        var (sim, dev) = Create();
        var rgb = new byte[X68Device.UserPicBytes];
        rgb[6 * 3] = 255;                 // F1 red
        rgb[41 * 3 + 2] = 255;            // Space blue
        sim.Sent.Clear();
        dev.WriteUserPicVerified(2, rgb);
        Assert.Equal(7, sim.Sent.Count(p => p[0] == SetUserPic));     // one picture = 7 packets, no repeats
        Assert.Equal(rgb, sim.State.UserPics[2][..X68Device.UserPicBytes]);

        var led = dev.ReadLed();
        dev.EnterUserPictureMode(led, 2);
        Assert.Equal((byte)X68LightMode.UserPicture, sim.State.Led[1]);
        Assert.Equal(2 << 4, sim.State.Led[4]);
        Assert.DoesNotContain(sim.Sent, p => p[0] is SetProfile or SetReset);
    }

    [Fact]
    public void Garbled_lighting_is_never_considered_a_valid_effect()
    {
        Assert.False(X68Lighting.IsPlausibleEffect(Convert.FromHexString("8704BF04FBFFFFFF")));  // read over the receiver
        Assert.False(X68Lighting.IsPlausibleEffect(Convert.FromHexString("8715040400000000")));  // screen colour (host-driven)
        Assert.True(X68Lighting.IsPlausibleEffect(Convert.FromHexString("870104040714A404")));   // steady green
        Assert.True(X68Lighting.IsPlausibleEffect(Convert.FromHexString("870D0404200000C8")));   // picture 3
    }
}
