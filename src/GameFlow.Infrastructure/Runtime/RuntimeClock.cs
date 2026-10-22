namespace GameFlow.Infrastructure.Runtime;

/// <summary>A UTC-shaped, monotonic timeline for mapping durations and scheduling.</summary>
internal sealed class RuntimeClock
{
    private readonly TimeProvider provider;
    private readonly DateTimeOffset origin;
    private readonly long started;

    internal RuntimeClock(TimeProvider? provider = null)
    {
        this.provider = provider ?? TimeProvider.System;
        origin = this.provider.GetUtcNow();
        started = this.provider.GetTimestamp();
    }

    internal DateTimeOffset Now => origin + provider.GetElapsedTime(started);
}
