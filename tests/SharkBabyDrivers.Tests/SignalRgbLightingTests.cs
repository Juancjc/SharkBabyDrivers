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
