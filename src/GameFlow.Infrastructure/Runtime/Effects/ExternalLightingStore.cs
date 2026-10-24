using GameFlow.Core.Pipeline;

namespace GameFlow.Infrastructure.Runtime.Effects;

/// <summary>What an external lighting request applies to.</summary>
public enum ExternalLightingScope
{
    /// <summary>Every controller.</summary>
    All,

    /// <summary>Every physical pad assigned to one virtual controller (slot).</summary>
    Slot,

    /// <summary>One physical pad.</summary>
    Device,
}

/// <summary>One addressable target: <see cref="ExternalLightingScope.All"/> ignores <see cref="Id"/>.</summary>
public readonly record struct ExternalLightingTarget(ExternalLightingScope Scope, string Id = "")
{
    public static ExternalLightingTarget All => new(ExternalLightingScope.All);
}

/// <summary>
/// Lighting requested by other programs through the local lighting API —
/// Home Assistant, a Wallpaper Engine helper script, anything that can
/// send an HTTP request.
///
/// <para>
/// In memory only, and every entry expires. A script that sets a colour
/// and then crashes must not leave the pad that colour forever, and a
/// restart of GameFlow must not resurrect a request from a session that
/// no longer exists. Callers keep a colour alive by repeating the request
/// before its time-to-live runs out, which is also how every dashboard
/// integration of this kind already behaves.
/// </para>
///
/// <para>
/// Lookups prefer the most specific entry: a device request beats its
/// slot's, which beats an "all" request — so a script can paint every pad
/// one colour and still single one out.
/// </para>
/// </summary>
public sealed class ExternalLightingStore(TimeProvider? timeProvider = null)
{
    /// <summary>Time-to-live when a request does not give one.</summary>
    public static readonly TimeSpan DefaultTimeToLive = TimeSpan.FromSeconds(10);

    /// <summary>Longest time-to-live accepted: an hour, so even a forgotten request ends.</summary>
    public static readonly TimeSpan MaximumTimeToLive = TimeSpan.FromHours(1);

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Lock gate = new();
    private readonly Dictionary<ExternalLightingTarget, Entry> entries = [];

    /// <summary>Sets (or replaces) the request for one target. The TTL is clamped to (0, <see cref="MaximumTimeToLive"/>].</summary>
    public void Set(ExternalLightingTarget target, ExternalLightRequest request, TimeSpan timeToLive)
    {
        var ttl = timeToLive <= TimeSpan.Zero
            ? DefaultTimeToLive
            : timeToLive > MaximumTimeToLive ? MaximumTimeToLive : timeToLive;

        lock (gate)
        {
            entries[Normalize(target)] = new Entry(request, clock.GetUtcNow() + ttl);
        }
    }

    /// <summary>
    /// Removes a request. Clearing <see cref="ExternalLightingScope.All"/>
    /// removes EVERY request, so one call hands every pad back.
    /// Returns whether anything was removed.
    /// </summary>
    public bool Clear(ExternalLightingTarget target)
    {
        lock (gate)
        {
            if (target.Scope == ExternalLightingScope.All)
            {
                var any = entries.Count > 0;
                entries.Clear();
                return any;
            }

            return entries.Remove(Normalize(target));
        }
    }

    /// <summary>
    /// The request that applies to <paramref name="deviceId"/> on
    /// <paramref name="slotId"/> right now, or null. Expired entries are
    /// dropped on the way.
    /// </summary>
    public ExternalLightRequest? Resolve(string slotId, string deviceId)
    {
        var now = clock.GetUtcNow();

        lock (gate)
        {
            if (entries.Count == 0)
            {
                return null;
            }

            return Find(new ExternalLightingTarget(ExternalLightingScope.Device, deviceId ?? string.Empty), now)
                ?? Find(new ExternalLightingTarget(ExternalLightingScope.Slot, slotId ?? string.Empty), now)
                ?? Find(ExternalLightingTarget.All, now);
        }
    }

    /// <summary>Number of unexpired requests. For the Options page and diagnostics.</summary>
    public int ActiveCount
    {
        get
        {
            var now = clock.GetUtcNow();
            lock (gate)
            {
                return entries.Values.Count(entry => entry.ExpiresAt > now);
            }
        }
    }

    /// <summary>Caller holds <see cref="gate"/>.</summary>
    private ExternalLightRequest? Find(ExternalLightingTarget target, DateTimeOffset now)
    {
        var key = Normalize(target);
        if (!entries.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= now)
        {
            _ = entries.Remove(key);
            return null;
        }

        return entry.Request;
    }

    /// <summary>Ids compare without regard to case, matching the device catalog.</summary>
    private static ExternalLightingTarget Normalize(ExternalLightingTarget target) =>
        target.Scope == ExternalLightingScope.All
            ? ExternalLightingTarget.All
            : target with { Id = (target.Id ?? string.Empty).ToUpperInvariant() };

    private readonly record struct Entry(ExternalLightRequest Request, DateTimeOffset ExpiresAt);
}
