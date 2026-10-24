using System.Collections.Concurrent;

namespace GameFlow.Infrastructure.Runtime.Effects;

/// <summary>
/// Latest game-requested motor state per slot. HIDMaestro callbacks write
/// here from their output-reader threads; the effect producer reads from
/// its own timer, so neither side blocks the other.
/// </summary>
public sealed class RumbleFeedbackStore
{
    private readonly ConcurrentDictionary<string, RumbleFeedback> feedback =
        new(StringComparer.Ordinal);

    public RumbleFeedbackStore()
        : this(new GameLightingStore())
    {
    }

    /// <summary>
    /// Creates the store with the lighting store the game's LED requests
    /// go to. The container supplies the shared singleton, so the effect
    /// producer and the slot runtime see the same instance.
    /// </summary>
    public RumbleFeedbackStore(GameLightingStore lighting)
    {
        Lighting = lighting ?? throw new ArgumentNullException(nameof(lighting));
    }

    /// <summary>
    /// The lighting half of what a game sends back. Reached through this
    /// store, rather than injected beside it, because whatever wires a
    /// slot's sink to its rumble already holds this store — the game's
    /// light requests arrive on the same virtual pad, at the same moment,
    /// and have to be routed by the same slot id.
    /// </summary>
    public GameLightingStore Lighting { get; }

    public void Set(string slotId, double lowFrequency, double highFrequency)
    {
        if (string.IsNullOrEmpty(slotId))
        {
            return;
        }

        feedback[slotId] = new RumbleFeedback(
            Math.Clamp(lowFrequency, 0d, 1d),
            Math.Clamp(highFrequency, 0d, 1d));
    }

    public RumbleFeedback Get(string slotId) =>
        !string.IsNullOrEmpty(slotId) && feedback.TryGetValue(slotId, out var value)
            ? value
            : default;

    public void Clear(string slotId)
    {
        if (!string.IsNullOrEmpty(slotId))
        {
            _ = feedback.TryRemove(slotId, out _);
        }
    }
}

public readonly record struct RumbleFeedback(double LowFrequency, double HighFrequency);
