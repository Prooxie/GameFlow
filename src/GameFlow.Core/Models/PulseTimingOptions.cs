namespace GameFlow.Core.Models;

public sealed record PulseTimingOptions
{
    public int HoldMs { get; init; }
    public int ReleaseMs { get; init; } 
    public int SettleMs { get; init; }
    public int PauseAfterReleaseMs { get; init; }

    public TimeSpan Hold => TimeSpan.FromMilliseconds(HoldMs);
    public TimeSpan Release => TimeSpan.FromMilliseconds(ReleaseMs);
    public TimeSpan Settle => TimeSpan.FromMilliseconds(SettleMs);
    public TimeSpan PauseAfterRelease => TimeSpan.FromMilliseconds(PauseAfterReleaseMs);
}
