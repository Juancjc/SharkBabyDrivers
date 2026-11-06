using SharkBabyDrivers.Core;
using SharkBabyDrivers.Core.Devices;
using SharkBabyDrivers.Core.Devices.R5;
using SharkBabyDrivers.Core.Hid;

namespace SharkBabyDrivers.Tests;
public sealed class R5Tests
{
    [Fact] public void OfficialFirmwareQueryIncludesWindowsReportId()
    {
        var frame=R5Protocol.Frame(16,0,129);
        Assert.Equal(65,frame.Length);
        Assert.Equal(new byte[]{0,0,0,2,16,0,129,0},frame[..8]);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task BackupUsesQueriesOnlyAndDecodesBothHeaders(bool shortHeader)
    {
        var sim=new R5Simulator{ShortHeader=shortHeader};
        using var device=new R5Device(sim,"test",R5Protocol.DonglePid){DelayMs=0};
        var snapshot=await device.ReadSnapshotAsync();var config=R5Config.FromSnapshot(snapshot);
        Assert.Equal(sim.Config.Dpi,config.Dpi);Assert.Equal("0.0.12.0",snapshot.Firmware);
        Assert.All(sim.Sent,p=>Assert.True(p[6]>=128));
        Assert.Equal(17,snapshot.Reports.Count);
    }
    [Fact] public async Task ApplyVerifiesAllSettingsAndBackupCanRestoreThem()
    {
        var sim=new R5Simulator();using var device=new R5Device(sim,"test",R5Protocol.DonglePid){DelayMs=0};
        var original=await device.ReadSnapshotAsync();var config=R5Config.FromSnapshot(original);
        config.Dpi[0]=800;config.DpiY[0]=850;config.ActiveDpi=2;config.Polling=128;
        config.AngleSnap=true;config.MotionSync=true;config.Ripple=true;config.Tracking=true;
        config.SleepSeconds=120;config.Debounce=4;config.Lod=135;config.Buttons[3]=[7,1,6];
        await device.ApplyAsync(config);
        Assert.Equal(800,sim.Config.Dpi[0]);Assert.Equal(128,sim.Config.Polling);
        Assert.Equal(new byte[]{7,1,6},sim.Config.Buttons[3]);
        await device.RestoreSnapshotAsync(original);
        Assert.Equal(1200,sim.Config.Dpi[0]);Assert.Equal(1,sim.Config.Polling);
    }
    [Fact] public async Task RejectsChangedProfileBeforeAnyWrite()
    {
        var sim=new R5Simulator();using var device=new R5Device(sim,"test",R5Protocol.DonglePid){DelayMs=0};
        var config=R5Config.FromSnapshot(await device.ReadSnapshotAsync());sim.Config.Profile=2;sim.Sent.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>device.ApplyAsync(config));
        Assert.All(sim.Sent,p=>Assert.True(p[6]>=128));
    }
    [Fact] public async Task RejectsIgnoredWritesWithoutClaimingSuccess()
    {
        var sim=new R5Simulator{IgnoreWrites=true};using var device=new R5Device(sim,"test",R5Protocol.DonglePid){DelayMs=0};
        var config=R5Config.FromSnapshot(await device.ReadSnapshotAsync());config.Dpi[0]=800;
        await Assert.ThrowsAsync<IOException>(()=>device.ApplyAsync(config));
    }
    [Fact] public async Task SleepingReceiverDoesNotProduceBackup()
    {
        using var device=new R5Device(new R5Simulator{Awake=false},"test",R5Protocol.DonglePid){DelayMs=0};
        await Assert.ThrowsAsync<DeviceAsleepException>(()=>device.ReadSnapshotAsync());
    }
    [Fact] public async Task PreservesExistingMacroAssignmentDuringSensorEdits()
    {
        var sim=new R5Simulator();sim.Config.Buttons[4]=[16,3,0,1,1];
        using var device=new R5Device(sim,"test",R5Protocol.DonglePid){DelayMs=0};
        var config=R5Config.FromSnapshot(await device.ReadSnapshotAsync());config.MotionSync=true;sim.Sent.Clear();
        await device.ApplyAsync(config);
        Assert.DoesNotContain(sim.Sent,p=>p[5]==3 && p[6]<128);
        Assert.Equal(new byte[]{16,3,0,1,1},sim.Config.Buttons[4]);
    }
    [Fact] public async Task MacroImportFailsBeforeChangingOtherSettings()
    {
        var sim=new R5Simulator();using var device=new R5Device(sim,"test",R5Protocol.DonglePid){DelayMs=0};
        var config=R5Config.FromSnapshot(await device.ReadSnapshotAsync());config.MotionSync=true;config.Buttons[4]=[16,3,0,1,1];sim.Sent.Clear();
        await Assert.ThrowsAsync<NotSupportedException>(()=>device.ApplyAsync(config));
        Assert.All(sim.Sent,p=>Assert.True(p[6]>=128));
    }
    [Fact] public void RestrictsWiredPollingAndRequiresLeftClick()
    {
        var c=new R5Config{Polling=128};Assert.Throws<InvalidDataException>(()=>c.Validate(true));c.Polling=1;
        c.Buttons[0]=[0,0];Assert.Throws<InvalidDataException>(()=>c.Validate(false));
    }
    [Fact] public void CatalogRecognizesOnlyApplicationPidsAndConfigCollection()
    {
        HidDeviceInfo Info(ushort pid,int len=65)=>new("test",0x373E,pid,0,0xFF00,1,0,0,len,"R5","");
        var driver=new R5Driver();Assert.True(driver.Matches([Info(0x46)]));Assert.True(driver.Matches([Info(0x47)]));
        Assert.False(driver.Matches([Info(0xB046)]));Assert.False(driver.Matches([Info(0x21)]));Assert.False(driver.Matches([Info(0x46,0)]));
        Assert.Contains(DeviceCatalog.Drivers,d=>d is R5Driver);
    }
}

