namespace Autofire.Core.Models;

public sealed record PulseTimingOptions
{
    public int HoldMs { get; init; } = 128;
    public int ReleaseMs { get; init; } = 32;
    public int SettleMs { get; init; }
    public int PauseAfterReleaseMs { get; init; }

    public TimeSpan Hold => TimeSpan.FromMilliseconds(HoldMs);
    public TimeSpan Release => TimeSpan.FromMilliseconds(ReleaseMs);
    public TimeSpan Settle => TimeSpan.FromMilliseconds(SettleMs);
    public TimeSpan PauseAfterRelease => TimeSpan.FromMilliseconds(PauseAfterReleaseMs);
}
