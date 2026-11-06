using SharkBabyDrivers.Core.Devices.X11;
using SharkBabyDrivers.Core.Hid;
using static SharkBabyDrivers.Core.Devices.X11.X11Protocol;

namespace SharkBabyDrivers.Tests;

public class X11WriteRecoveryTests
{
    private static X11Device Device(ScriptedX11Transport transport) =>
        new(transport, "x11-recovery-sim") { Settle = TimeSpan.Zero, WakeTimeout = TimeSpan.Zero };

    [Theory]
    [InlineData(RidDpi)]
    [InlineData(RidLight)]
    [InlineData(RidPoll)]
    public void Dropped_configuration_write_is_retried_until_the_actual_report_matches(byte lostReport)
    {
        var transport = new ScriptedX11Transport();
        using var device = Device(transport);
        var before = transport.Config;
        var desired = before.Clone();
        desired.SetStageDpi(0, 1200);
        desired.LightMode = X11LightMode.Static;
        desired.LightColor = new Rgb(0xAB, 0x12, 0xCD);
        desired.PollingHz = 500;
        var dropped = false;
        transport.AcceptWrite = report =>
        {
            if (report[0] != lostReport || dropped) return true;
            dropped = true;
            return false;
        };

        device.WriteConfig(desired, before);

        Assert.True(dropped);
        Assert.Equal(2, transport.Writes.Count(report => report[0] == lostReport));
        Assert.True(desired.SameAs(device.ReadConfig()));
    }

    [Theory]
    [InlineData(RidDpi)]
    [InlineData(RidLight)]
    [InlineData(RidPoll)]
    public void Permanently_dropped_report_fails_instead_of_claiming_success(byte lostReport)
    {
        var transport = new ScriptedX11Transport { AcceptWrite = report => report[0] != lostReport };
        using var device = Device(transport);
        var before = transport.Config;
        var desired = before.Clone();
        desired.SetStageDpi(0, 1200);
        desired.LightMode = X11LightMode.Static;
        desired.PollingHz = 500;

        var error = Assert.Throws<IOException>(() => device.WriteConfig(desired, before));

        Assert.Contains($"0x{lostReport:X2}", error.Message);
        Assert.Equal(4, transport.Writes.Count(report => report[0] == lostReport));
        Assert.False(desired.SameAs(transport.Config));
    }

    [Theory]
    [InlineData(RidDpi)]
    [InlineData(RidLight)]
    [InlineData(RidPoll)]
    public void Stale_verification_response_causes_a_retry(byte staleReport)
    {
        var transport = new ScriptedX11Transport();
        using var device = Device(transport);
        var before = transport.Config;
        var oldFrame = staleReport switch
        {
            RidDpi => before.DpiFrame(),
            RidLight => before.LightFrame(),
            _ => before.PollFrame(),
        };
        var desired = before.Clone();
        desired.SetStageDpi(0, 1200);
        desired.LightMode = X11LightMode.Static;
        desired.PollingHz = 500;
        var stale = false;
        transport.ReadReply = (rid, response) =>
        {
            if (rid != staleReport || stale) return response;
            stale = true;
            return (byte[])oldFrame.Clone();
        };

        device.WriteConfig(desired, before);

        Assert.True(stale);
        Assert.Equal(2, transport.Writes.Count(report => report[0] == staleReport));
        Assert.True(desired.SameAs(device.ReadConfig()));
    }

    [Fact]
    public void Transient_USB_write_error_is_retried()
    {
        var transport = new ScriptedX11Transport();
        using var device = Device(transport);
        var before = transport.Config;
        var desired = before.Clone();
        desired.PollingHz = 500;
        var failed = false;
        transport.AcceptWrite = report =>
        {
            if (report[0] == RidPoll && !failed)
            {
                failed = true;
                throw new HidException("Simulated USB write failure.");
            }
            return true;
        };

        device.WriteConfig(desired, before);

        Assert.Equal(2, transport.Writes.Count(report => report[0] == RidPoll));
        Assert.Equal(500, transport.Config.PollingHz);
    }

    [Fact]
    public void Forced_configuration_restore_keeps_DPI_before_lighting_and_polling()
    {
        var transport = new ScriptedX11Transport();
        using var device = Device(transport);
        var saved = transport.Config;
        var drifted = saved.Clone();
        drifted.SetStageDpi(0, 1200);
        drifted.LightMode = X11LightMode.Static;
        drifted.PollingHz = 125;
        transport.ReplaceConfig(drifted);

        device.WriteConfig(saved, null);

        Assert.Equal(new byte[] { RidDpi, RidLight, RidPoll }, transport.Writes.Select(report => report[0]));
        Assert.True(saved.SameAs(transport.Config));
    }

    [Fact]
    public void Sleeping_mouse_cannot_complete_a_fresh_configuration_read()
    {
        var transport = new ScriptedX11Transport { Awake = false };
        using var device = Device(transport);

        Assert.Throws<MouseAsleepException>(() => device.ReadConfig());

        Assert.Empty(transport.Writes);
    }
}
