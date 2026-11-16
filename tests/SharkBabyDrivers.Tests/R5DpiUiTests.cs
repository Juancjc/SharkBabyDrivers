using SharkBabyDrivers.ViewModels;

namespace SharkBabyDrivers.Tests;

public sealed class R5DpiUiTests
{
    [Fact]
    public void SeparateAxesSurviveLoadingAndLinkingSynchronizesBothDirections()
    {
        var changes = 0;
        var stage = new R5DpiVm(0, 800, 1200, () => changes++, _ => { });
        Assert.False(stage.Linked);
        stage.X = 1600;
        Assert.Equal(1200, stage.Y);
        stage.Linked = true;
        Assert.Equal(1600, stage.Y);
        stage.Y = 2400;
        Assert.Equal(2400, stage.X);
        stage.X = 3200;
        Assert.Equal(3200, stage.Y);
        stage.Linked = false;
        stage.Y = 800;
        Assert.Equal(3200, stage.X);
        Assert.Equal(5, changes);
    }

    [Fact]
    public void SelectionUsesOneBasedStageAndRefreshDoesNotRequestWrites()
    {
        byte selected = 0;
        var changes = 0;
        var stage = new R5DpiVm(2, 800, 800, () => changes++, value => selected = value);
        stage.SetActive(3);
        Assert.True(stage.IsActive);
        Assert.Equal("Em uso", stage.ActiveLabel);
        Assert.Equal(0, selected);
        stage.SetActive(1);
        Assert.False(stage.IsActive);
        stage.IsActive = true;
        Assert.Equal(3, selected);
        Assert.Equal(0, changes);
    }
}
