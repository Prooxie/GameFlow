using Autofire.Core.Enums;
using System.Text.Json.Serialization;

namespace Autofire.Core.Models.Rules;

/// <summary>
/// Joy2Key-style combo mapping: press a single source button (or button-like
/// input) and have the engine emit a combination of one or more virtual buttons,
/// each with its own optional pre-press delay and hold duration.
///
/// Example use cases:
///   • Press "Y" once → emit Up + LeftShoulder simultaneously (jump-cancel).
///   • Press "RB" → emit South after 100 ms, then East after 50 ms (combo string).
///   • Press "L3" → hold Up + South for 500 ms (charge attack).
///
/// The rule fires on the rising edge of the source. While the combo is "active",
/// each step's PreDelayMs is honoured before the corresponding virtual button is
/// pressed; HoldMs determines how long that virtual press is held before release.
/// Steps execute in array order.  When SuppressSourceButton is true, the source's
/// own state is dropped from the virtual snapshot.
/// </summary>
public sealed record ButtonComboRule : MappingRule
{
    [JsonPropertyName("sourceButton")]
    public ButtonId SourceButton { get; init; } = ButtonId.None;

    /// <summary>
    /// Ordered set of virtual button presses to emit when the source rises.
    /// </summary>
    [JsonPropertyName("steps")]
    public IReadOnlyList<ButtonComboStep> Steps { get; init; } = [];

    [JsonPropertyName("suppressSourceButton")]
    public bool SuppressSourceButton { get; init; }

    /// <summary>
    /// When true, the combo plays even when the source is released early.
    /// When false (default), releasing the source aborts any unfinished steps.
    /// </summary>
    [JsonPropertyName("playToCompletion")]
    public bool PlayToCompletion { get; init; }

    /// <summary>
    /// Optional inter-step gap inserted automatically between steps when the
    /// step's own PreDelayMs is zero. Useful for guaranteeing distinct frames
    /// for games that coalesce same-frame inputs.
    /// </summary>
    [JsonPropertyName("interStepGapMs")]
    public int InterStepGapMs { get; init; }
}

/// <summary>
/// A single output button press inside a <see cref="ButtonComboRule"/>.
/// </summary>
public sealed record ButtonComboStep
{
    [JsonPropertyName("button")]
    public ButtonId Button { get; init; } = ButtonId.None;

    /// <summary>
    /// Delay (in ms) before this step's button is pressed, measured from the
    /// rising edge of the source.
    /// </summary>
    [JsonPropertyName("preDelayMs")]
    public int PreDelayMs { get; init; }

    /// <summary>
    /// How long this step's button is held (in ms) once it is pressed.
    /// Zero means hold for the same duration as the source button.
    /// </summary>
    [JsonPropertyName("holdMs")]
    public int HoldMs { get; init; } = 60;
}
