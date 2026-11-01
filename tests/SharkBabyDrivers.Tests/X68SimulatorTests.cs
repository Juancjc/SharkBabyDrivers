using SharkBabyDrivers.Core.Devices.X68;
using static SharkBabyDrivers.Core.Devices.X68.X68Protocol;

namespace SharkBabyDrivers.Tests;

/// <summary>
/// Full backup → edit → write → read-back cycles against a strict simulator of the RY5088 firmware
/// (it rejects bad checksums, out-of-order pages and forbidden commands).
/// </summary>
public class X68SimulatorTests
{
    private static (X68Simulator Sim, X68Device Dev) Create(uint id = 2370, int fw = 0x0410, int active = 1)
    {
        var state = X68Simulator.FactoryImage(id, fw);
        state.ActiveProfile = active;
        // Something non-trivial in every profile so mix-ups between profiles would show.
        for (var p = 0; p < Profiles; p++)
        {
            state.SetHeValue(p, HeOp.Actuation, 30, 100 + p * 10);
            X68Action.Key((byte)(0x04 + p)).Write(state.Keys[p][0], 30);
        }
        X68Action.Macro(2, X68MacroMode.WhileHeld).Write(state.Keys[0][0], 40);
        state.Macros[2] = X68Macro.Encode([new(0x0B, true, 20), new(0x0B, false, 20)], 2);
        var sim = new X68Simulator(state);
        return (sim, new X68Device(sim, "sim") { TransferDelayMs = 0, WriteSettleMs = 0 });
    }

    private static void AssertSame(X68Image a, X68Image b)
    {
        Assert.Equal(a.ActiveProfile, b.ActiveProfile);
        Assert.Equal(a.Led[1..8], b.Led[1..8]);
        Assert.Equal(a.KbOption[2..6], b.KbOption[2..6]);
        Assert.Equal(a.Sleep[8..16], b.Sleep[8..16]);
        Assert.Equal(a.Report[2], b.Report[2]);
        Assert.Equal(a.Debounce[1], b.Debounce[1]);
        Assert.Equal(a.AutoOs[1], b.AutoOs[1]);
        Assert.Equal(a.FnWin, b.FnWin);
        Assert.Equal(a.FnMac, b.FnMac);
        for (var i = 0; i < 5; i++) Assert.Equal(a.UserPics[i][..378], b.UserPics[i][..378]);
        for (var p = 0; p < Profiles; p++)
        {
            for (var s = 0; s < SonProfiles; s++) Assert.Equal(a.Keys[p][s], b.Keys[p][s]);
            foreach (var op in ProfileOps) Assert.Equal(a.He(p, op), b.He(p, op));
        }
        Assert.Equal(a.Macros.Keys, b.Macros.Keys);
        foreach (var k in a.Macros.Keys)
            Assert.Equal(X68Macro.Decode(a.Macros[k]), X68Macro.Decode(b.Macros[k]), new MacroComparer());
    }

    private sealed class MacroComparer : IEqualityComparer<(List<X68Macro.Step>, int)>
    {
        public bool Equals((List<X68Macro.Step>, int) x, (List<X68Macro.Step>, int) y) => x.Item2 == y.Item2 && x.Item1.SequenceEqual(y.Item1);
        public int GetHashCode((List<X68Macro.Step>, int) obj) => obj.Item2;
    }

    [Fact]
    public void Backup_reads_everything_and_restores_the_active_profile()
    {
        var (sim, dev) = Create(active: 1);
        var img = dev.ReadImage();
        AssertSame(sim.State, img);
        Assert.Equal(1, sim.State.ActiveProfile);
        // The only writes of a backup are profile switches.
        Assert.All(sim.Sent.Where(p => p[0] < 0x80), p => Assert.Equal(SetProfile, p[0]));
    }

    [Fact]
    public void Unchanged_image_writes_nothing()
    {
        var (sim, dev) = Create();
        var img = dev.ReadImage();
        sim.Sent.Clear();
        dev.WriteImage(img.Clone(), img);
        Assert.Empty(sim.Sent);
    }

    [Fact]
    public void Edits_in_every_area_reach_the_device_exactly()
    {
        var (sim, dev) = Create(active: 0);
        var before = dev.ReadImage();
        var img = before.Clone();

        // keys in a non-active profile, Fn layer, macro, lighting, options
        X68Action.Consumer(0x00CD).Write(img.Keys[3][0], 12);
        X68Action.Key(0x06, KeyMods.LCtrl).Write(img.FnWin, 13);
        img.Macros[5] = X68Macro.Encode([new(0x04, true, 300), new(0x04, false, 15)], 4);
        X68Action.Macro(5, X68MacroMode.RepeatCount).Write(img.Keys[2][0], 14);
        img.LightMode = X68LightMode.Breath;
        img.LightColor = 0x123456;
        img.LightSpeed = 3;
        img.PollingHz = 1000;
        img.DebounceValue = 5;
        img.WasdSwap = true;
        img.SleepSeconds24 = 300;
        img.AutoOsDetect = true;
        img.UserPics[2][0] = 0xFF;

        // Hall effect in two different profiles, incl. RT, DKS and snap tap
        img.SetHeValue(0, HeOp.Actuation, 20, img.Raw(0.5));
        img.SetKeyMode(0, 20, KeyMode.Normal, rapidTrigger: true);
        img.SetHeValue(0, HeOp.RtPress, 20, img.Raw(0.15));
        img.SetKeyMode(2, 21, KeyMode.Dks, rapidTrigger: false);
        img.SetHeValue(2, HeOp.DksTravel, 21, img.Raw(1.2));
        img.SetDksStage(2, 21, 0, 0b01_00_10_01);
        img.SetDksStage(2, 21, 3, 0b11_00_00_00);
        img.SetKeyMode(2, 9, KeyMode.SnapTap, false);
        img.SetKeyMode(2, 15, KeyMode.SnapTap, false);
        img.He(2, HeOp.SnapPartner)[9] = 15;
        img.He(2, HeOp.SnapPartner)[15] = 9;
        img.He(1, HeOp.SwitchType)[7] = 3;
        img.SetHeValue(3, HeOp.TopDeadZone, 8, 12);
        img.He(3, HeOp.MtTime)[8] = 25;

        dev.WriteImage(img, before);
        AssertSame(img, sim.State);
        Assert.Equal(0, sim.State.ActiveProfile); // back on the profile the user had
        AssertSame(img, dev.ReadImage());
    }

    [Fact]
    public void Restore_of_a_backup_snapshot_recreates_the_device_state()
    {
        var (sim, dev) = Create();
        var snapshot = dev.ReadImage().ToSnapshot("k", "m");
        // Someone changes the keyboard afterwards…
        var (_, other) = Create();
        var changed = other.ReadImage();
        changed.LightMode = X68LightMode.Off;
        X68Action.Disabled.Write(changed.Keys[0][0], 30);
        dev.WriteImage(changed, null);
        // …then the backup is restored.
        dev.WriteImage(X68Image.FromSnapshot(snapshot), null);
        AssertSame(X68Image.FromSnapshot(snapshot), sim.State);
    }

    [Fact]
    public void Old_firmware_skips_top_dead_zone()
    {
        var (sim, dev) = Create(fw: 0x0350);
        var img = dev.ReadImage();
        Assert.False(img.HasTopDeadZone);
        Assert.DoesNotContain(sim.Sent, p => p[0] == GetMultiMagnetism && p[1] == (byte)HeOp.TopDeadZone);
        Assert.Equal(100, img.Divisor);
    }

    [Fact]
    public void Unknown_keyboard_is_refused_before_any_write()
    {
        var state = X68Simulator.FactoryImage();
        state.DeviceId = 9999;
        var sim = new X68Simulator(state);
        var dev = new X68Device(sim, "x") { TransferDelayMs = 0, WriteSettleMs = 0 };
        Assert.Throws<NotSupportedException>(() => dev.ReadImage());
        Assert.DoesNotContain(sim.Sent, p => p[0] < 0x80);
    }
}
