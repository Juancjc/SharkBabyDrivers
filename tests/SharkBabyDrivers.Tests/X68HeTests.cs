using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Devices.X68;
using SharkBabyDrivers.Core.Hid;
using static SharkBabyDrivers.Core.Devices.X68.X68Protocol;

namespace SharkBabyDrivers.Tests;

public class X68HeTests
{
    public static IEnumerable<object[]> HeConnections()
    {
        foreach (var id in new uint[] { 2270, 2472, 2902 })
        {
            yield return [id, false];
            yield return [id, true];
        }
    }

    private static (X68Simulator Sim, X68Device Device) Create(uint id, bool wireless)
    {
        var state = X68Simulator.FactoryImage(id);
        state.ActiveProfile = 2;
        for (var profile = 0; profile < Profiles; profile++)
        {
            state.SetHeValue(profile, HeOp.Actuation, 30, 100 + profile * 10);
            X68Action.Key((byte)(0x04 + profile)).Write(state.Keys[profile][0], 30);
        }
        var sim = new X68Simulator(state);
        return (sim, new X68Device(sim, "sim-he", wireless ? PidDongle : PidX68HeWired,
            wireless ? LinkKind.Dongle : LinkKind.Wired) { TransferDelayMs = 0, WriteSettleMs = 0 });
    }

    [Fact]
    public void Wired_HE_detection_requires_the_vendor_configuration_collection()
    {
        var config = new HidDeviceInfo("sim-he&mi_02", VendorId, PidX68HeWired, 0,
            ConfigUsagePage, ConfigUsage, 0, 0, 65, "X68 HE", "sim");
        var driver = new X68Driver();

        Assert.True(driver.Matches([config]));
        Assert.False(driver.Matches([config with { Usage = EventUsage }]));
        Assert.False(driver.Matches([config with { FeatureReportLength = 0 }]));
        Assert.False(driver.Matches([config with { VendorId = 0x1234 }]));
        Assert.False(driver.Matches([config with { ProductId = 0x502A }]));
    }

    [Theory]
    [InlineData(2270u)]
    [InlineData(2472u)]
    [InlineData(2902u)]
    public void HE_models_preserve_identity_and_have_unique_keyboard_slots(uint id)
    {
        Assert.True(X68Model.IsKnown(id));
        var model = X68Model.ForId(id);

        Assert.Equal(id, model.Id);
        Assert.StartsWith("X68 HE", model.Name);
        Assert.True(model.Caps.Count >= 66);
        Assert.Equal(model.Caps.Count, model.Caps.Select(cap => cap.Slot).Distinct().Count());
        Assert.All(model.Caps, cap => Assert.InRange(cap.Slot, 0, Slots - 1));
        Assert.NotNull(model.FnSlot());
        Assert.Equal(1, model.SlotOf(0x29));
        Assert.Equal(PidX68HeWired, X68Simulator.FactoryImage(id).ToSnapshot("sim", "HE").ProductId);
    }

    [Theory]
    [InlineData(2270u)]
    [InlineData(2472u)]
    public void Original_HE_defaults_preserve_the_dedicated_media_slots(uint id)
    {
        var model = X68Model.ForId(id);

        Assert.Equal(59, model.FnSlot());
        Assert.Equal(X68Action.Consumer(233), model.DefaultAction(90));
        Assert.Equal(X68Action.Consumer(234), model.DefaultAction(91));
        Assert.Equal(X68Action.Consumer(226), model.DefaultAction(92));
        for (var slot = 90; slot <= 92; slot++)
            Assert.Equal(X68Action.Disabled, X68Model.Rev2370.DefaultAction(slot));
    }

    [Fact]
    public void HE_V2_defaults_match_the_vendor_PRO_V2_matrices()
    {
        var model = X68Model.ForId(2902);

        Assert.Equal(65, model.FnSlot());
        Assert.Equal(X68Model.Rev2901.DefaultKeys, model.DefaultKeys);
        Assert.Equal(X68Model.Rev2901.DefaultFnWin, model.DefaultFnWin);
        Assert.Equal(X68Model.Rev2901.DefaultFnMac, model.DefaultFnMac);
    }

    [Fact]
    public void HE_Mac_defaults_preserve_revision_specific_media_and_LED_actions()
    {
        var original = X68Model.ForId(2270).DefaultFnMac;
        var revised = X68Model.ForId(2472).DefaultFnMac;

        Assert.Equal(X68Action.Disabled, X68Action.Read(original, 91));
        Assert.Equal(X68Action.Consumer(226), X68Action.Read(revised, 91));
        Assert.Equal(X68Action.Disabled, X68Action.Read(original, 94));
        Assert.Equal(new X68Action(13, 2, 2, 0), X68Action.Read(revised, 94));
    }

    [Theory]
    [MemberData(nameof(HeConnections))]
    public async Task HE_backup_identifies_the_model_and_retains_connection_metadata(uint id, bool wireless)
    {
        var (sim, device) = Create(id, wireless);
        using (device)
        {
            if (!wireless) Assert.Equal("Attack Shark X68 HE", device.Model);
            var snapshot = await device.ReadSnapshotAsync();
            var image = X68Image.FromSnapshot(snapshot);

            Assert.Equal("Attack Shark X68 HE", device.Model);
            Assert.Equal(device.Model, snapshot.Model);
            Assert.Equal(wireless ? PidDongle : PidX68HeWired, snapshot.ProductId);
            Assert.Equal(VendorId, snapshot.VendorId);
            Assert.Equal(id, image.DeviceId);
            Assert.Equal(2, image.ActiveProfile);
            Assert.Equal(sim.State.FnWin, image.FnWin);
            Assert.Equal(sim.State.FnMac, image.FnMac);
            for (var profile = 0; profile < Profiles; profile++)
            {
                Assert.Equal(sim.State.Keys[profile][0], image.Keys[profile][0]);
                Assert.Equal(profile == 2, X68Device.HeWasRead(image, profile));
            }
            Assert.False(device.CanSwitchProfiles);
            Assert.Throws<InvalidOperationException>(() => device.SelectProfile(0));
            Assert.All(sim.Sent, packet => Assert.InRange(packet[0], (byte)0x80, (byte)0xEF));
            Assert.Equal(2, sim.State.ActiveProfile);
        }
    }

    [Theory]
    [MemberData(nameof(HeConnections))]
    public void HE_verified_lighting_write_keeps_the_active_profile_and_HE_unchanged(uint id, bool wireless)
    {
        var (sim, device) = Create(id, wireless);
        using (device)
        {
            var before = device.ReadImage();
            var desired = before.Clone();
            desired.LightBrightness = 1;
            sim.Sent.Clear();

            device.WriteImageVerified(desired, before);

            Assert.Equal(wireless ? GetUsbVersion : GetProfile, sim.Sent[0][0]);
            Assert.Equal(SetLedParam, Assert.Single(sim.Sent.Where(packet => packet[0] < 0x80))[0]);
            Assert.Equal(1, sim.State.LightBrightness);
            Assert.Equal(2, sim.State.ActiveProfile);
            for (var profile = 0; profile < Profiles; profile++)
                Assert.Equal(100 + profile * 10, sim.State.HeValue(profile, HeOp.Actuation, 30));
        }
    }

    [Theory]
    [MemberData(nameof(HeConnections))]
    public void HE_verified_travel_write_only_changes_the_active_profile(uint id, bool wireless)
    {
        var (sim, device) = Create(id, wireless);
        using (device)
        {
            var before = device.ReadImage();
            var desired = before.Clone();
            desired.SetHeValue(2, HeOp.Actuation, 30, 25);
            desired.SetHeValue(0, HeOp.Actuation, 30, 50);
            sim.Sent.Clear();

            device.WriteImageVerified(desired, before);

            Assert.Equal(wireless ? GetUsbVersion : GetProfile, sim.Sent[0][0]);
            Assert.DoesNotContain(sim.Sent, packet => packet[0] == SetProfile);
            Assert.Equal(25, sim.State.HeValue(2, HeOp.Actuation, 30));
            Assert.Equal(100, sim.State.HeValue(0, HeOp.Actuation, 30));
            Assert.Equal(2, sim.State.ActiveProfile);
        }
    }

    [Theory]
    [MemberData(nameof(HeConnections))]
    public void HE_never_reads_or_writes_wireless_sleep_timers(uint id, bool wireless)
    {
        var (sim, device) = Create(id, wireless);
        using (device)
        {
            var originalSleep = (byte[])sim.State.Sleep.Clone();
            var before = device.ReadImage();
            var desired = before.Clone();
            // A file imported from another revision can contain timers even when the controls are hidden.
            desired.SleepSeconds24 = 300;
            desired.DeepSleepSeconds24 = 600;
            desired.SleepSecondsBt = 300;
            desired.DeepSleepSecondsBt = 600;
            desired.LightBrightness = 1;

            device.WriteImageVerified(desired, before);

            Assert.DoesNotContain(sim.Sent, packet => packet[0] is GetSleepTime or SetSleepTime);
            Assert.Equal(originalSleep, sim.State.Sleep);
            Assert.Equal(1, sim.State.LightBrightness);
        }
    }

    [Theory]
    [MemberData(nameof(HeConnections))]
    public void Direct_HE_sleep_read_never_sends_a_command(uint id, bool wireless)
    {
        var (sim, device) = Create(id, wireless);
        using (device)
        {
            // The cable PID identifies HE before the lighting coordinator can request timers.
            // A common receiver needs the keyboard's identity first.
            if (wireless) device.ReadImage();
            sim.Sent.Clear();

            device.ReadSleep();

            Assert.Empty(sim.Sent);
        }
    }

    [Theory]
    [MemberData(nameof(HeConnections))]
    public void HE_physical_profile_change_rejects_writes_before_any_SET(uint id, bool wireless)
    {
        var (sim, device) = Create(id, wireless);
        using (device)
        {
            var before = device.ReadImage();
            var desired = before.Clone();
            desired.LightBrightness = 1;
            desired.SetHeValue(2, HeOp.Actuation, 30, 25);
            sim.State.ActiveProfile = 1;
            sim.Sent.Clear();

            Assert.Throws<IOException>(() => device.WriteImageVerified(desired, before));

            Assert.DoesNotContain(sim.Sent, packet => packet[0] < 0x80);
            Assert.Equal(110, sim.State.HeValue(1, HeOp.Actuation, 30));
        }
    }

    [Theory]
    [InlineData(2370u, false)]
    [InlineData(2370u, true)]
    [InlineData(2901u, false)]
    [InlineData(2901u, true)]
    public void PRO_identity_keeps_the_existing_saved_profile_group(uint id, bool wireless)
    {
        var sim = new X68Simulator(X68Simulator.FactoryImage(id));
        using var device = new X68Device(sim, "sim-pro", wireless ? PidDongle : PidX68ProHeWired,
            wireless ? LinkKind.Dongle : LinkKind.Wired) { TransferDelayMs = 0, WriteSettleMs = 0 };

        device.ReadImage();

        Assert.Equal("Attack Shark X68 PRO HE", device.Model);
    }
}
