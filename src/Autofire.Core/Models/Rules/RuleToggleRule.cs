using Autofire.Core.Enums;
using System.Text.Json.Serialization;

namespace Autofire.Core.Models.Rules;

/// <summary>
/// Toggles the enabled/disabled state of one or more *other* rules
/// each time the trigger button is clicked (rising edge).
///
/// <para>
/// Semantics:
/// <list type="bullet">
///   <item>On the rising edge of <see cref="SourceButton"/>, every rule
///         whose id is in <see cref="TargetRuleIds"/> flips state.</item>
///   <item>Already-enabled targets become disabled; already-disabled
///         targets become enabled. Toggles happen in lock-step — they
///         either all flip on or all flip off relative to whatever
///         state they were in when the trigger fired.</item>
///   <item>The toggle is applied as a *runtime overlay* maintained by
///         the pipeline — the target rule records themselves stay
///         immutable. This means the user's "default" Enabled flag
///         in the profile JSON stays authoritative across app launches;
///         toggles reset to that default on every restart, which is the
///         normal mental model for a "press-this-to-mute" macro.</item>
/// </list>
/// </para>
///
/// <para>
/// Use cases:
/// <list type="bullet">
///   <item>Disable a stick-autofire bind during cutscenes by pressing
///         Touchpad once; press again to re-enable.</item>
///   <item>Bundle a "stealth mode" — one click silences several
///         button-remap rules at once.</item>
/// </list>
/// </para>
/// </summary>
public sealed record RuleToggleRule : MappingRule
{
    /// <summary>
    /// Physical button whose rising edge flips the targets. None ⇒
    /// rule is a no-op.
    /// </summary>
    [JsonPropertyName("sourceButton")]
    public ButtonId SourceButton { get; init; } = ButtonId.None;

    /// <summary>
    /// Ids of the rules whose state this toggle controls. Ids that
    /// don't resolve to a rule in the active profile are silently
    /// ignored — useful so a deleted target doesn't break unrelated
    /// rules.
    /// </summary>
    [JsonPropertyName("targetRuleIds")]
    public IReadOnlyList<string> TargetRuleIds { get; init; } = [];

    /// <summary>
    /// When true, the source button's own state is dropped from the
    /// virtual output so the game doesn't see the trigger press
    /// itself. Off by default — most users want the button to still
    /// register on the game side as well.
    /// </summary>
    [JsonPropertyName("suppressSourceButton")]
    public bool SuppressSourceButton { get; init; }
}
