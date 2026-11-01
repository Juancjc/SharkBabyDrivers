using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Devices.X68;
using static SharkBabyDrivers.Core.Devices.X68.X68Protocol;

namespace SharkBabyDrivers.Tests;

public class X68DongleSafetyTests
{
    private sealed class InterceptTransport(X68Simulator simulator, Func<byte[], bool> forward) : IX68Transport
    {
        public event Action<byte[]>? Event { add => simulator.Event += value; remove => simulator.Event -= value; }
        public void Send(byte[] packet) { if (forward(packet)) simulator.Send(packet); }
        public byte[] Receive() => simulator.Receive();
        public void Dispose() => simulator.Dispose();
    }

    private static (X68Simulator Sim, X68Device Device) Create(int active = 1)
    {
        var state = X68Simulator.FactoryImage();
        state.ActiveProfile = active;
        for (var profile = 0; profile < Profiles; profile++)
            state.SetHeValue(profile, HeOp.Actuation, 30, 100 + profile * 10);
        var sim = new X68Simulator(state);
        return (sim, new X68Device(sim, "sim-dongle", PidDongle, LinkKind.Dongle)
        {
            TransferDelayMs = 0,
            WriteSettleMs = 0,
        });
    }

    [Fact]
    public void Wireless_backup_reads_only_active_HE_without_sending_any_SET()
    {
        var (sim, device) = Create();
        var image = device.ReadImage();

        Assert.Equal(1, image.ActiveProfile);
        for (var profile = 0; profile < Profiles; profile++)
            Assert.Equal(profile == 1, X68Device.HeWasRead(image, profile));
        Assert.DoesNotContain(sim.Sent, packet => packet[0] < 0x80);
    }

    [Fact]
    public void Physical_profile_change_rejects_all_writes_before_the_first_SET()
    {
        var (sim, device) = Create();
        var before = device.ReadImage();
        var desired = before.Clone();
        desired.LightBrightness = 1;
        desired.SetHeValue(1, HeOp.Actuation, 30, 25);
        sim.State.ActiveProfile = 2;
        sim.Sent.Clear();

        var error = Assert.Throws<IOException>(() => device.WriteImageVerified(desired, before));

        Assert.Contains("Leia o teclado novamente", error.Message);
        Assert.DoesNotContain(sim.Sent, packet => packet[0] < 0x80);
        Assert.Equal(120, sim.State.HeValue(2, HeOp.Actuation, 30));
    }

    [Fact]
    public void Image_requesting_a_different_active_profile_is_rejected_before_any_SET()
    {
        var (sim, device) = Create();
        var before = device.ReadImage();
        var desired = before.Clone();
        desired.ActiveProfile = 2;
        desired.LightBrightness = 1;
        sim.Sent.Clear();

        Assert.Throws<IOException>(() => device.WriteImageVerified(desired, before));
        Assert.DoesNotContain(sim.Sent, packet => packet[0] < 0x80);
        Assert.Equal(1, sim.State.ActiveProfile);
    }

    [Fact]
    public void Wireless_LED_write_is_verified_without_switching_profiles_or_touching_HE()
    {
        var (sim, device) = Create();
        var before = device.ReadImage();
        var desired = before.Clone();
        desired.LightBrightness = 1;
        sim.Sent.Clear();

        device.WriteImageVerified(desired, before);

        var packet = Assert.Single(sim.Sent.Where(packet => packet[0] < 0x80));
        Assert.Equal(SetLedParam, packet[0]);
        Assert.Equal(1, sim.State.LightBrightness);
        Assert.Equal(1, sim.State.ActiveProfile);
    }

    [Fact]
    public void Macro_verification_accepts_zero_padded_GET_pages()
    {
        var (sim, device) = Create();
        var before = device.ReadImage();
        var desired = before.Clone();
        desired.Macros[5] = X68Macro.Encode([new(0x04, true, 10), new(0x04, false, 10)], 1);
        X68Action.Macro(5, X68MacroMode.RepeatCount).Write(desired.Keys[1][0], 30);
        sim.Sent.Clear();

        device.WriteImageVerified(desired, before);

        Assert.Single(sim.Sent.Where(packet => packet[0] == SetMacro));
        Assert.DoesNotContain(sim.Sent, packet => packet[0] == SetProfile);
        var actual = X68Macro.Decode(device.ReadImage().Macros[5]);
        var expected = X68Macro.Decode(desired.Macros[5]);
        Assert.Equal(expected.Item1, actual.Item1);
        Assert.Equal(expected.Item2, actual.Item2);
    }

    [Fact]
    public void Wireless_write_never_sends_HE_for_an_inactive_profile()
    {
        var (sim, device) = Create();
        var before = device.ReadImage();
        var desired = before.Clone();
        desired.SetHeValue(2, HeOp.Actuation, 30, 25);
        desired.LightBrightness = 1;
        sim.Sent.Clear();

        device.WriteImageVerified(desired, before);

        Assert.DoesNotContain(sim.Sent, packet => packet[0] is SetProfile or SetMultiMagnetism);
        Assert.Equal(110, sim.State.HeValue(1, HeOp.Actuation, 30));
        Assert.Equal(120, sim.State.HeValue(2, HeOp.Actuation, 30));
    }

    [Fact]
    public void Wireless_backup_rejects_a_profile_switch_during_the_read()
    {
        var state = X68Simulator.FactoryImage();
        state.ActiveProfile = 1;
        var sim = new X68Simulator(state);
        var profileQueries = 0;
        var transport = new InterceptTransport(sim, packet =>
        {
            if (packet[0] == GetProfile && ++profileQueries == 2) state.ActiveProfile = 2;
            return true;
        });
        var device = new X68Device(transport, "sim-dongle", PidDongle, LinkKind.Dongle)
        {
            TransferDelayMs = 0,
            WriteSettleMs = 0,
        };

        Assert.Throws<IOException>(() => device.ReadImage());
        Assert.DoesNotContain(sim.Sent, packet => packet[0] < 0x80);
    }

    [Fact]
    public void Wireless_verification_retries_a_write_that_did_not_reach_the_keyboard()
    {
        var state = X68Simulator.FactoryImage();
        var sim = new X68Simulator(state);
        var ledWrites = 0;
        var transport = new InterceptTransport(sim, packet => packet[0] != SetLedParam || ++ledWrites != 1);
        var device = new X68Device(transport, "sim-dongle", PidDongle, LinkKind.Dongle)
        {
            TransferDelayMs = 0,
            WriteSettleMs = 0,
        };
        var before = device.ReadImage();
        var desired = before.Clone();
        desired.LightBrightness = 1;

        device.WriteImageVerified(desired, before);

        Assert.Equal(2, ledWrites);
        Assert.Equal(1, state.LightBrightness);
        Assert.DoesNotContain(sim.Sent, packet => packet[0] == SetProfile);
    }

    [Theory]
    [InlineData(LinkKind.Wired)]
    [InlineData(LinkKind.Dongle)]
    public void Keyboard_factory_reset_is_blocked_before_reaching_any_transport(LinkKind link)
    {
        var sim = new X68Simulator(X68Simulator.FactoryImage());
        var device = new X68Device(sim, "sim", link: link) { TransferDelayMs = 0, WriteSettleMs = 0 };

        Assert.Throws<InvalidOperationException>(device.FactoryReset);
        Assert.Empty(sim.Sent);
    }
}
