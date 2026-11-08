using SharkBabyDrivers.Core.Devices.X68;

namespace SharkBabyDrivers.Tests;

public class ProfileResetTests
{
    [Fact]
    public void ResetUsesPhysicalActiveProfileAndPreservesOtherSettings()
    {
        var original = new X68Image { DeviceId = 2901, ActiveProfile = 0 };
        original.Keys[1][0][0] = 4;
        var current = original.Clone();
        current.ActiveProfile = 1;
        current.Keys[1][0][0] = 99;
        current.Keys[0][0][0] = 42;
        current.Led[0] = 17;
        var restored = current.WithActiveProfileRestored(original);
        Assert.Equal(1, restored.ActiveProfile);
        Assert.Equal(4, restored.Keys[1][0][0]);
        Assert.Equal(42, restored.Keys[0][0][0]);
        Assert.Equal(17, restored.Led[0]);
        Assert.Equal(99, current.Keys[1][0][0]);
        restored.Keys[1][0][0] = 8;
        Assert.Equal(4, original.Keys[1][0][0]);
    }

    [Fact]
    public void ResetKeepsCurrentHeWhenOriginalDidNotReadActiveProfile()
    {
        var current = new X68Image { DeviceId = 2901, ActiveProfile = 1 };
        current.HallEffect[1][X68Protocol.HeOp.Actuation] = new byte[] { 25 };
        var original = new X68Image { DeviceId = 2901 };
        var restored = current.WithActiveProfileRestored(original);
        Assert.Equal(25, restored.HallEffect[1][X68Protocol.HeOp.Actuation][0]);
    }

    [Fact]
    public void ResetRejectsBackupFromDifferentKeyboard()
    {
        Assert.Throws<InvalidDataException>(() => new X68Image { DeviceId = 2901 }
            .WithActiveProfileRestored(new X68Image { DeviceId = 2370 }));
    }
}
