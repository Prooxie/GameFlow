using System.Collections.Concurrent;
using GameFlow.Core.Pipeline;

namespace GameFlow.Infrastructure.Runtime.Effects;

/// <summary>
/// The latest lighting each slot's game asked for, with when it asked.
///
/// <para>
/// Same shape and threading as <see cref="RumbleFeedbackStore"/>: the
/// HIDMaestro output thread writes, the effect producer reads on its own
/// timer, and neither blocks the other. Unlike rumble, a lighting report
/// is a partial update — a game that sets only the player LEDs has not
/// asked for the lightbar to change — so each report is MERGED into the
/// slot's state instead of replacing it.
/// </para>
///
/// <para>
/// The colour carries its own timestamp because freshness is what the
/// "follow the game" rule turns on: a game that keeps sending colours is
/// in control, one that set a colour once at start-up hands the bar back
/// after the idle timeout.
/// </para>
/// </summary>
public sealed class GameLightingStore(TimeProvider? timeProvider = null)
{
    /// <summary>DualShock 4 flash durations count in 10 ms steps (255 ≈ 2.5 s).</summary>
    private const double FlashUnitSeconds = 0.01;

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<string, Entry> entries = new(StringComparer.Ordinal);

    /// <summary>Merges one decoded report into the slot's state.</summary>
    public void Update(string slotId, GameLighting lighting)
    {
        if (string.IsNullOrEmpty(slotId) || lighting is null || !lighting.HasAny)
        {
            return;
        }

        var now = clock.GetUtcNow();
        entries.AddOrUpdate(
            slotId,
            _ => Merge(default, lighting, now),
            (_, existing) => Merge(existing, lighting, now));
    }

    /// <summary>The slot's current game lighting, or null when its game never sent any.</summary>
    public GameLightState? Get(string slotId)
    {
        if (string.IsNullOrEmpty(slotId) || !entries.TryGetValue(slotId, out var entry))
        {
            return null;
        }

        var age = entry.Color is null
            ? double.PositiveInfinity
            : Math.Max(0d, (clock.GetUtcNow() - entry.ColorAt).TotalSeconds);

        return new GameLightState(
            entry.Color,
            age,
            entry.FlashOn * FlashUnitSeconds,
            entry.FlashOff * FlashUnitSeconds,
            entry.PlayerIndicator,
            entry.MicLed is { } mic ? (MicLedState)Math.Clamp((int)mic, 0, 2) : null,
            entry.LedBrightness is { } level ? (IndicatorBrightnessLevel)Math.Clamp((int)level, 0, 2) : null);
    }

    /// <summary>Forgets a slot — its virtual pad is gone, so nothing it asked for still applies.</summary>
    public void Clear(string slotId)
    {
        if (!string.IsNullOrEmpty(slotId))
        {
            _ = entries.TryRemove(slotId, out _);
        }
    }

    private static Entry Merge(Entry existing, GameLighting update, DateTimeOffset now) => existing with
    {
        Color = update.Color ?? existing.Color,
        ColorAt = update.Color is not null ? now : existing.ColorAt,
        FlashOn = update.FlashOn ?? existing.FlashOn,
        FlashOff = update.FlashOff ?? existing.FlashOff,
        PlayerIndicator = update.PlayerIndicator ?? existing.PlayerIndicator,
        MicLed = update.MicLed ?? existing.MicLed,
        LedBrightness = update.LedBrightness ?? existing.LedBrightness,
    };

    private readonly record struct Entry
    {
        public LightColor? Color { get; init; }

        public DateTimeOffset ColorAt { get; init; }

        public byte FlashOn { get; init; }

        public byte FlashOff { get; init; }

        public byte? PlayerIndicator { get; init; }

        public byte? MicLed { get; init; }

        public byte? LedBrightness { get; init; }
    }
}
