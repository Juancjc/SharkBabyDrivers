using SharkBabyDrivers.Core.Devices.X11;

namespace SharkBabyDrivers.Tests;

public class X11PreviewTests
{
    [Fact]
    public async Task Preview_reads_edits_and_restores_without_a_Hid_connection()
    {
        using var device = X11Device.CreatePreview();
        var snapshot = await device.ReadSnapshotAsync();
        Assert.Equal("x11-demo-preview", snapshot.DeviceKey);
        Assert.Equal(4, snapshot.Reports.Count);

        var original = device.ReadConfig();
        var desired = original.Clone();
        desired.SetStageDpi(0, 1200);
        desired.AngleSnap = !original.AngleSnap;
        desired.LightMode = X11LightMode.Static;
        desired.LightColor = new Rgb(0xAB, 0x12, 0xCD);
        desired.PollingHz = 500;
        device.WriteConfig(desired, original);

        var read = device.ReadConfig();
        Assert.Equal(1200, read.GetStageDpi(0));
        Assert.Equal(desired.AngleSnap, read.AngleSnap);
        Assert.Equal(desired.LightColor, read.LightColor);
        Assert.Equal(500, read.PollingHz);

        await device.RestoreSnapshotAsync(snapshot);
        Assert.Equal(original.DpiFrame(), device.ReadConfig().DpiFrame());
        Assert.Equal(original.LightFrame(), device.ReadConfig().LightFrame());
        Assert.Equal(original.PollFrame(), device.ReadConfig().PollFrame());
    }

    [Fact]
    public void Preview_instances_and_returned_reports_have_independent_state()
    {
        using var first = X11Device.CreatePreview();
        using var second = X11Device.CreatePreview();
        var config = first.ReadConfig();
        config.SetStageDpi(0, 3200);
        Assert.Equal(800, first.ReadConfig().GetStageDpi(0));
        first.WriteConfig(config, null);
        Assert.Equal(3200, first.ReadConfig().GetStageDpi(0));
        Assert.Equal(800, second.ReadConfig().GetStageDpi(0));
        first.FactoryReset();
        Assert.Equal(800, first.ReadConfig().GetStageDpi(0));
    }

    [Fact]
    public void Preview_accepts_shortcuts_and_macros_but_keeps_them_write_only()
    {
        using var device = X11Device.CreatePreview();
        var slots = X11Buttons.FactoryDefaults();
        slots[(int)X11Button.Forward] = ButtonAction.Key(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x04);
        device.WriteButtons(slots);
        device.WriteMacro(X11Buttons.MacroSlotId(X11Button.Backward), new MacroDefinition("A", [new MacroStep(0x04, true, 20), new MacroStep(0x04, false, 20)]));

        Assert.Throws<InvalidOperationException>(() => device.ReadReport(X11Protocol.RidButtons, X11Protocol.LenButtons, 1));
        Assert.Equal(800, device.ReadConfig().GetStageDpi(0));
    }

    [Fact]
    public async Task Preview_snapshot_emits_battery_status_for_the_mouse_page()
    {
        using var device = X11Device.CreatePreview();
        X11Event.Battery? battery = null;
        device.EventReceived += e => { if (e is X11Event.Battery b) battery = b; };
        await device.ReadSnapshotAsync();
        Assert.Equal(new X11Event.Battery(86, BatteryState.Discharging), battery);
    }
}
