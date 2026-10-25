namespace GameFlow.App.Services;

/// <summary>Applies the user refresh limit to display frames. Late frames do not change that limit.</summary>
public sealed class DashboardFrameClock
{
    private TimeSpan? previous;

    /// <summary>Accepts the latest frame when the configured interval has elapsed. It never queues missed frames.</summary>
    public bool ShouldRefresh(TimeSpan timestamp, int refreshHz)
    {
        var interval = TimeSpan.FromSeconds(1d / Math.Clamp(refreshHz, 30, 1000));
        if (previous is { } last && timestamp >= last && timestamp - last + TimeSpan.FromTicks(10) < interval)
        {
            return false;
        }
        previous = timestamp;
        return true;
    }
}
