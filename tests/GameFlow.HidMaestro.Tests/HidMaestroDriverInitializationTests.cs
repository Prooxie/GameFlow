using Xunit;

namespace GameFlow.HidMaestro.Tests;

public sealed class HidMaestroDriverInitializationTests
{
    [WindowsFact]
    public void MultipleCallsInstallOnce()
    {
        var installs = 0;
        var initialization = new HidMaestroDriverInitialization(() => installs++);
        Assert.Equal(0, installs);

        Assert.True(initialization.TryEnsureInstalled(out var failure));
        Assert.Null(failure);
        Assert.True(initialization.TryEnsureInstalled(out failure));
        Assert.Null(failure);
        Assert.Equal(1, installs);
    }

    [WindowsFact]
    public void FailureIsActionableAndDoesNotRepeatGlobalSweep()
    {
        var installs = 0;
        var initialization = new HidMaestroDriverInitialization(() =>
        {
            installs++;
            throw new InvalidOperationException("denied");
        });

        Assert.False(initialization.TryEnsureInstalled(out var failure));
        Assert.Contains("denied", failure);
        Assert.Contains("restart", failure);
        Assert.False(initialization.TryEnsureInstalled(out var secondFailure));
        Assert.Equal(failure, secondFailure);
        Assert.Equal(1, installs);
    }
}
