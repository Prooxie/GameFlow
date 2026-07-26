using System.Text.Json.Serialization;
using GameFlow.Core.Enums;

namespace GameFlow.Core.Models.Rules;

/// <summary>
/// SOCD (Simultaneous Opposite Cardinal Direction) cleaning for one
/// opposite-direction button pair — e.g. Left/Right, or Up/Down. When
/// both are held at once, exactly one mode decides what the virtual
/// output reports; when at most one is held there's no conflict and
/// both pass straight through untouched.
///
/// <para>
/// One rule instance covers ONE pair — "across every key you've mapped"
/// from the spec means adding one <see cref="SocdCleanRule"/> per
/// opposite pair (Left/Right, Up/Down, and any custom pair), not that a
/// single rule spans all of them at once.
/// </para>
/// </summary>
public sealed record SocdCleanRule : MappingRule
{
    [JsonPropertyName("negativeButton")]
    public ButtonId NegativeButton { get; init; } = ButtonId.None;

    [JsonPropertyName("positiveButton")]
    public ButtonId PositiveButton { get; init; } = ButtonId.None;

    [JsonPropertyName("socdMode")]
    public SocdMode SocdMode { get; init; } = SocdMode.LastWins;
}

/// <summary>
/// <list type="bullet">
///   <item><b>LastWins</b> (Snap Tap) — whichever of the pair was pressed most recently (and is still held) wins; the other is suppressed.</item>
///   <item><b>FirstWins</b> — whichever was pressed first holds the win until IT releases, even if the other is pressed and still held.</item>
///   <item><b>Neutral</b> — both are suppressed while overlapping; neither reports pressed.</item>
/// </list>
/// </summary>
public enum SocdMode
{
    LastWins,
    FirstWins,
    Neutral
}
