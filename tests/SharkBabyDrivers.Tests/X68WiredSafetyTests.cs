using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Devices.X68;
using static SharkBabyDrivers.Core.Devices.X68.X68Protocol;

namespace SharkBabyDrivers.Tests;

public class X68WiredSafetyTests
{
    private sealed class InterceptTransport(X68Simulator simulator, Action<byte[]> inspect) : IX68Transport
    {
        public event Action<byte[]>? Event { add => simulator.Event += value; remove => simulator.Event -= value; }
        public void Send(byte[] packet) { inspect(packet); simulator.Send(packet); }
        public byte[] Receive() => simulator.Receive();
        public void Dispose() => simulator.Dispose();
    }

    private static (X68Simulator Sim, X68Device Device) Create(int active = 2, uint id = 2901)
    {
        var state = X68Simulator.FactoryImage(id);
        state.ActiveProfile = active;
        for (var profile = 0; profile < Profiles; profile++)
        {
            state.SetHeValue(profile, HeOp.Actuation, 30, 100 + profile * 10);
            X68Action.Key((byte)(0x04 + profile)).Write(state.Keys[profile][0], 30);
        }
        var sim = new X68Simulator(state);
        return (sim, new X68Device(sim, "sim-wired") { TransferDelayMs = 0, WriteSettleMs = 0 });
    }

    [Fact]
    public void Profile_switching_stays_blocked_until_identity_is_read()
    {
        var (sim, device) = Create(id: 2370);

        Assert.False(device.CanSwitchProfiles);
        Assert.Throws<InvalidOperationException>(() => device.SelectProfile(1));
        Assert.Empty(sim.Sent);

        device.ReadImage();

        Assert.True(device.CanSwitchProfiles);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Revision_2901_wired_backup_is_GET_only_and_reads_HE_only_for_the_active_profile(int active)
    {
        var (sim, device) = Create(active);

        var image = device.ReadImage();

        Assert.False(device.CanSwitchProfiles);
        Assert.Equal(active, image.ActiveProfile);
        Assert.Equal(active, sim.State.ActiveProfile);
        for (var profile = 0; profile < Profiles; profile++)
        {
            Assert.Equal(sim.State.Keys[profile][0], image.Keys[profile][0]);
            Assert.Equal(profile == active, X68Device.HeWasRead(image, profile));
        }
        Assert.All(sim.Sent, packet => Assert.InRange(packet[0], (byte)0x80, (byte)0xEF));
    }

    [Fact]
    public void Revision_2901_wired_profile_switch_is_rejected_even_after_identification()
    {
        var (sim, device) = Create();
        device.ReadImage();
        sim.Sent.Clear();

        Assert.Throws<InvalidOperationException>(() => device.SelectProfile(0));

        Assert.Empty(sim.Sent);
        Assert.Equal(2, sim.State.ActiveProfile);
    }

    [Fact]
    public void Failed_revision_2901_wired_backup_never_restores_a_profile_in_finally()
    {
        var sim = new X68Simulator(X68Simulator.FactoryImage(2901));
        sim.State.ActiveProfile = 2;
        var expected = new IOException("simulated read failure after the active profile");
        var transport = new InterceptTransport(sim, packet =>
        {
            if (packet[0] == GetKeyMatrix && packet[1] == 0) throw expected;
        });
        var device = new X68Device(transport, "sim-wired") { TransferDelayMs = 0, WriteSettleMs = 0 };

        var actual = Assert.Throws<IOException>(() => device.ReadImage());

        Assert.Same(expected, actual);
        Assert.False(device.CanSwitchProfiles);
        Assert.DoesNotContain(sim.Sent, packet => packet[0] < 0x80);
        Assert.Equal(2, sim.State.ActiveProfile);
    }

    [Fact]
    public void Revision_2901_identity_is_kept_even_if_a_later_identification_query_fails()
    {
        var sim = new X68Simulator(X68Simulator.FactoryImage(2901));
        var transport = new InterceptTransport(sim, packet =>
        {
            if (packet[0] == GetRfVersion) throw new IOException("simulated RF version failure");
        });
        var device = new X68Device(transport, "sim-wired") { TransferDelayMs = 0, WriteSettleMs = 0 };

        Assert.Throws<IOException>(() => device.ReadImage());
        Assert.False(device.CanSwitchProfiles);
        Assert.Throws<InvalidOperationException>(() => device.SelectProfile(1));
        Assert.DoesNotContain(sim.Sent, packet => packet[0] < 0x80);
    }

    [Fact]
    public void Revision_2901_wired_LED_write_checks_the_physical_profile_without_switching_it()
    {
        var (sim, device) = Create();
        var before = device.ReadImage();
        var desired = before.Clone();
        desired.LightBrightness = 1;
        sim.Sent.Clear();

        device.WriteImageVerified(desired, before);

        Assert.Equal(GetProfile, sim.Sent[0][0]);
        Assert.Equal(SetLedParam, Assert.Single(sim.Sent.Where(packet => packet[0] < 0x80))[0]);
        Assert.Equal(2, sim.State.ActiveProfile);
    }

    [Fact]
    public void Revision_2901_wired_HE_writes_reach_only_the_active_profile()
    {
        var (sim, device) = Create();
        var before = device.ReadImage();
        var desired = before.Clone();
        desired.SetHeValue(2, HeOp.Actuation, 30, 25);
        desired.SetHeValue(0, HeOp.Actuation, 30, 50);
        sim.Sent.Clear();

        device.WriteImageVerified(desired, before);

        Assert.Equal(GetProfile, sim.Sent[0][0]);
        Assert.DoesNotContain(sim.Sent, packet => packet[0] == SetProfile);
        Assert.Equal(25, sim.State.HeValue(2, HeOp.Actuation, 30));
        Assert.Equal(100, sim.State.HeValue(0, HeOp.Actuation, 30));
    }

    [Fact]
    public void Revision_2901_wired_physical_profile_change_rejects_all_writes_before_any_SET()
    {
        var (sim, device) = Create();
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
