using GameFlow.Core.Enums;
using System.Text.Json.Serialization;

namespace GameFlow.Core.Models.Rules;

/// <summary>
/// Reduces several source buttons down to a single target button — the
/// mapping grid's "one output, many inputs" row. List any number of
/// physical (or already-remapped) buttons as <see cref="Sources"/>, pick
/// a <see cref="Mode"/> from the six built-in strategies, or drop to
/// <see cref="CombineMode.Script"/> for a Lua expression when none of the
/// presets fit.
///
/// <para>
/// Sources are read from the same <c>buttons</c> map every other rule
/// kind writes into, so a combine row naturally sees the output of any
/// <see cref="ButtonRemapRule"/> or <see cref="ButtonComboRule"/> that
/// already ran this tick — chaining rules is "just" listing the upstream
/// rule's target button as a source here. Execution order follows the
/// same fixed rule-kind ordering as everything else in
/// <see cref="Pipeline.ControllerMappingPipeline"/>; see that file for
/// exactly where this rule kind's block sits.
/// </para>
/// </summary>
public sealed record MultiSourceCombineRule : MappingRule
{
    /// <summary>
    /// The buttons this row reduces to one value. Order only matters for
    /// authoring clarity — every mode except <see cref="CombineMode.Script"/>
    /// is order-independent by construction (see <see cref="CombineMode"/>'s
    /// remarks on why "priority" isn't offered as a separate mode).
    /// </summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<ButtonId> Sources { get; init; } = [];

    /// <summary>
    /// Named <c>Strategy</c> rather than <c>Mode</c> on purpose — the base
    /// <see cref="MappingRule.Mode"/> (Passthrough / DoNothing / Modify) is
    /// already inherited and still applies to this rule kind the same as
    /// every other one; a same-named property here would have silently
    /// hidden it instead of extending it.
    /// </summary>
    [JsonPropertyName("strategy")]
    public CombineMode Strategy { get; init; } = CombineMode.Any;

    [JsonPropertyName("targetButton")]
    public ButtonId TargetButton { get; init; } = ButtonId.None;

    /// <summary>
    /// Lua expression used only when <see cref="Mode"/> is
    /// <see cref="CombineMode.Script"/>; ignored for every other mode.
    /// Must define <c>function evaluate(ctx) return ... end</c> — see
    /// <see cref="Scripting.LuaScriptEngine.EvaluateCombine"/> for the
    /// exact contract, including what <c>ctx</c> exposes.
    /// </summary>
    [JsonPropertyName("scriptCode")]
    public string ScriptCode { get; init; } = string.Empty;

    /// <summary>
    /// When true, every listed source is dropped from the virtual output
    /// (unless it's also the target button itself) — the game only ever
    /// sees <see cref="TargetButton"/>, never the raw sources underneath it.
    /// </summary>
    [JsonPropertyName("suppressSources")]
    public bool SuppressSources { get; init; }
}
