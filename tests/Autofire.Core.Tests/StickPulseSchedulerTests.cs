using Autofire.Core.Models;
using Autofire.Core.Pipeline;
using Xunit;

namespace Autofire.Core.Tests;

public sealed class StickPulseSchedulerTests
{
    [Fact]
    public void Tick_ShouldPulseDesiredVector()
    {
        var scheduler = new StickPulseScheduler(new PulseTimingOptions
        {
            HoldMs = 100,
            ReleaseMs = 50
        });

        var start = DateTimeOffset.UtcNow;

        scheduler.SetDesired(new StickVector(1f, 0f), start);

        var first = scheduler.Tick(start);
        var second = scheduler.Tick(start.AddMilliseconds(40));
        var third = scheduler.Tick(start.AddMilliseconds(110));
        var fourth = scheduler.Tick(start.AddMilliseconds(170));

        Assert.Equal(new StickVector(1f, 0f), first.Value);
        Assert.Equal(new StickVector(1f, 0f), second.Value);
        Assert.Equal(StickVector.Zero, third.Value);
        Assert.Equal(new StickVector(1f, 0f), fourth.Value);
    }

    [Fact]
    public void Tick_ShouldReturnZeroImmediatelyWhenDesiredBecomesZero()
    {
        var scheduler = new StickPulseScheduler(new PulseTimingOptions());

        var now = DateTimeOffset.UtcNow;
        scheduler.SetDesired(new StickVector(0.75f, 0.15f), now);

        _ = scheduler.Tick(now);

        scheduler.SetDesired(StickVector.Zero, now.AddMilliseconds(10));
        var stopped = scheduler.Tick(now.AddMilliseconds(12));

        Assert.Equal(StickVector.Zero, stopped.Value);
    }

    [Fact]
    public void SetDesired_ShouldNotResetPulsePhase_WhenStickDirectionChangesDuringActivePulse()
    {
        var scheduler = new StickPulseScheduler(new PulseTimingOptions
        {
            HoldMs = 100,
            ReleaseMs = 50
        });

        var start = DateTimeOffset.UtcNow;
        scheduler.SetDesired(new StickVector(1f, 0f), start);

        var first = scheduler.Tick(start);
        scheduler.SetDesired(new StickVector(0f, 1f), start.AddMilliseconds(40));
        var duringHold = scheduler.Tick(start.AddMilliseconds(60));
        var release = scheduler.Tick(start.AddMilliseconds(110));

        Assert.Equal(new StickVector(1f, 0f), first.Value);
        Assert.Equal(new StickVector(0f, 1f), duringHold.Value);
        Assert.Equal(StickVector.Zero, release.Value);
    }
}
