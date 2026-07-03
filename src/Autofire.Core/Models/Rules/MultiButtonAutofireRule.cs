using Autofire.Core.Enums;
using System.Text.Json.Serialization;

namespace Autofire.Core.Models.Rules;

/// <summary>
/// Looping multi-button autofire. While the source button is held,
/// the executor walks through <see cref="Steps"/> in order, pressing
/// each step's <see cref="MultiButtonAutofireStep.TargetButton"/> for
/// its <see cref="MultiButtonAutofireStep.HoldMs"/>, releasing it for
/// its <see cref="MultiButtonAutofireStep.ReleaseMs"/>, waiting
/// <see cref="MultiButtonAutofireStep.DelayAfterMs"/>, then moving on
/// to the next step. After the last step the timeline wraps back to
/// step 0 and the cycle repeats — so this differs from
/// <see cref="ButtonComboRule"/> (which plays the sequence exactly once
/// per rising edge) by being a continuous loop driven by the held
/// source.
///
/// <para>
/// Use cases:
/// <list type="bullet">
///   <item>Rotating combo (Square → Triangle → Circle → repeat) for a
///         fighting-game macro held with a single shoulder button.</item>
///   <item>Spam several abilities at staggered cadences from a single
///         hold.</item>
///   <item>Drum-style alternating presses on two buttons (40 ms hold,
///         40 ms release, 0 delay).</item>
/// </list>
/// </para>
///
/// <para>
/// When the source is released, the executor immediately stops
/// pressing any of the step buttons — no carry-over from the
/// half-finished step. (Unlike ButtonComboRule there's no
/// PlayToCompletion option; looping autofire is inherently
/// hold-driven.)
/// </para>
/// </summary>
public sealed record MultiButtonAutofireRule : MappingRule
{
    /// <summary>
    /// The physical (input-side) button whose hold drives the loop.
    /// When this button transitions to pressed the loop starts; when
    /// it goes back to released the loop stops and the current step's
    /// target is released.
    /// </summary>
    [JsonPropertyName("sourceButton")]
    public ButtonId SourceButton { get; init; } = ButtonId.None;

    /// <summary>
    /// Ordered list of (target, hold, release, delay) tuples executed
    /// in order and looped. An empty list makes the rule a no-op.
    /// </summary>
    [JsonPropertyName("steps")]
    public IReadOnlyList<MultiButtonAutofireStep> Steps { get; init; } = [];

    /// <summary>
    /// When true, the source button is dropped from the virtual
    /// output so the game doesn't see the trigger press itself —
    /// only the looped step buttons.
    /// </summary>
    [JsonPropertyName("suppressSourceButton")]
    public bool SuppressSourceButton { get; init; }
}

/// <summary>
/// One element of a <see cref="MultiButtonAutofireRule"/>'s timeline.
///
/// <para>
/// Each step contributes three timing values:
/// <list type="number">
///   <item><b>HoldMs</b> — how long the target button is held pressed
///         once this step becomes active.</item>
///   <item><b>ReleaseMs</b> — how long the target is held released
///         immediately after the press, before the inter-step delay
///         starts. Often set to 0 for back-to-back step transitions;
///         non-zero gives the game time to register the release.</item>
///   <item><b>DelayAfterMs</b> — additional dead time after the
///         release phase, before the next step's press begins. Useful
///         for spacing out parts of a combo on games that coalesce
///         same-frame inputs.</item>
/// </list>
/// </para>
///
/// <para>
/// Step total duration = HoldMs + ReleaseMs + DelayAfterMs.
/// Loop period = sum of all steps' totals.
/// </para>
/// </summary>
public sealed record MultiButtonAutofireStep
{
    [JsonPropertyName("targetButton")]
    public ButtonId TargetButton { get; init; } = ButtonId.None;

    /// <summary>How long the target button is held pressed (ms).</summary>
    [JsonPropertyName("holdMs")]
    public int HoldMs { get; init; } = 30;

    /// <summary>
    /// How long the target stays released after the press, before the
    /// post-step delay starts (ms). Default 30 gives a clean
    /// press/release edge that most games will register as a discrete
    /// input.
    /// </summary>
    [JsonPropertyName("releaseMs")]
    public int ReleaseMs { get; init; } = 30;

    /// <summary>
    /// Extra dead time inserted after the release phase, before the
    /// next step starts (ms). 0 = step transitions are back-to-back.
    /// </summary>
    [JsonPropertyName("delayAfterMs")]
    public int DelayAfterMs { get; init; }
}
