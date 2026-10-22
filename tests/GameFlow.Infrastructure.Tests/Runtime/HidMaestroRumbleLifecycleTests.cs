using GameFlow.Infrastructure.Runtime.HidMaestro;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

public sealed class HidMaestroRumbleLifecycleTests
{
    [Fact]
    public void StopAndTeardown_PublishesZeroBeforeControllerIsReleased()
    {
        var calls = new List<string>();

        HidMaestroRumbleLifecycle.StopAndTeardown(
            (low, high) => calls.Add($"rumble:{low}:{high}"),
            () => calls.Add("teardown"));

        Assert.Equal(["rumble:0:0", "teardown"], calls);
    }

    [Fact]
    public void StopAndTeardown_StillStopsOtherSubscribersAndReleasesControllerWhenFeedbackHandlerFails()
    {
        var tornDown = false;
        var stopped = false;
        Exception? failure = null;
        Action<double, double> feedback = (_, _) => throw new InvalidOperationException("subscriber failed");
        feedback += (low, high) => stopped = low == 0d && high == 0d;

        HidMaestroRumbleLifecycle.StopAndTeardown(
            feedback, () => tornDown = true, exception => failure = exception);

        Assert.True(stopped);
        Assert.True(tornDown);
        Assert.IsType<InvalidOperationException>(failure);
    }
}
