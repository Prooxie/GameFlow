using GameFlow.App.Services;
using Xunit;

namespace GameFlow.Infrastructure.Tests.ViewModels;

/// <summary>Checks frame pacing without converting a temporary UI stall into a lower refresh rate.</summary>
public sealed class DashboardFrameClockTests
{
    [Fact]
    public void ShouldRefreshTemporaryStallKeepsConfiguredRate()
    {
        var clock = new DashboardFrameClock();
        Assert.True(clock.ShouldRefresh(TimeSpan.Zero, 60));

        Assert.True(clock.ShouldRefresh(TimeSpan.FromMilliseconds(200), 60));
        Assert.True(clock.ShouldRefresh(TimeSpan.FromMilliseconds(217), 60));
    }

    [Fact]
    public void ShouldRefreshHighRefreshDisplayUsesEveryFrame()
    {
        var clock = new DashboardFrameClock();

        for (var i = 0; i < 100; i++)
        {
            Assert.True(clock.ShouldRefresh(TimeSpan.FromSeconds(i / 144d), 144));
        }
    }

    [Fact]
    public void ShouldRefreshLowerUserLimitSkipsExtraDisplayFrames()
    {
        var clock = new DashboardFrameClock();
        Assert.True(clock.ShouldRefresh(TimeSpan.Zero, 30));

        Assert.False(clock.ShouldRefresh(TimeSpan.FromSeconds(1 / 60d), 30));
        Assert.True(clock.ShouldRefresh(TimeSpan.FromSeconds(2 / 60d), 30));
    }
}
