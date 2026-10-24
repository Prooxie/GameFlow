using GameFlow.Core.Models;
using GameFlow.Core.Pipeline;
using GameFlow.Infrastructure.Runtime;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

public sealed class RuntimeClockTests
{
    [Fact]
    public void ClockAdjustmentsDoNotExtendAnAutofirePhase()
    {
        var time = new AdjustableTime();
        var clock = new RuntimeClock(time);
        var pulse = new BinaryPulseScheduler(new PulseTimingOptions
        {
            HoldMs = 30,
            ReleaseMs = 30
        });
        pulse.SetDesired(true, clock.Now);
        Assert.True(pulse.Tick(clock.Now).IsPressed);
        time.WallTime -= TimeSpan.FromHours(2);
        time.Timestamp += 31;
        Assert.False(pulse.Tick(clock.Now).IsPressed);
        time.WallTime += TimeSpan.FromDays(5);
        time.Timestamp += 31;
        Assert.True(pulse.Tick(clock.Now).IsPressed);
    }

    private sealed class AdjustableTime : TimeProvider
    {
        internal DateTimeOffset WallTime = DateTimeOffset.UnixEpoch;
        internal long Timestamp;
        public override DateTimeOffset GetUtcNow() => WallTime;
        public override long GetTimestamp() => Timestamp;
        public override long TimestampFrequency => 1000;
    }
}
