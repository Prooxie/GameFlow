using GameFlow.Infrastructure.Runtime;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

public sealed class RuntimeCoordinatorSlotRebuildTests
{
    [Fact]
    public void DirtyLastSlotDisabled_BypassesDebounceSoCachedOutputsArePruned()
    {
        var shouldRebuild = RuntimeCoordinator.ShouldRebuildSlots(
            hasEnabledSlots: false,
            profileSwitched: false,
            slotsDirty: true,
            debounceElapsed: false);

        Assert.True(shouldRebuild);
    }

    /// <summary>
    /// A disposed provider while the app runs is a tick failure, not the end of the loop.
    /// Only shutdown may end the loop on it, or mapping stops with no message.
    /// </summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void IsShutdownDisposalDisposedProviderReturnsStopping(bool isStopping, bool expected)
    {
        var exception = new ObjectDisposedException("input source");

        var result = RuntimeCoordinator.IsShutdownDisposal(exception, isStopping);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsShutdownDisposalOtherExceptionReturnsFalse()
    {
        var exception = new InvalidOperationException("tick failed");

        var result = RuntimeCoordinator.IsShutdownDisposal(exception, isStopping: true);

        Assert.False(result);
    }

    [Fact]
    public void DirtyEnabledSlots_RemainDebounced()
    {
        var shouldRebuild = RuntimeCoordinator.ShouldRebuildSlots(
            hasEnabledSlots: true,
            profileSwitched: false,
            slotsDirty: true,
            debounceElapsed: false);

        Assert.False(shouldRebuild);
    }
}
