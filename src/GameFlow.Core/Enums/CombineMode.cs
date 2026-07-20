namespace GameFlow.Core.Enums;

/// <summary>
/// How a <see cref="Models.Rules.MultiSourceCombineRule"/> reduces several
/// source buttons down to a single target button's pressed/released state.
///
/// <para>
/// <see cref="Any"/>, <see cref="All"/>, <see cref="None"/>, and
/// <see cref="ExactlyOne"/> are the four standard boolean reductions —
/// deliberately non-overlapping (for a single boolean target, anything
/// framed as "priority" or "highest wins" collapses to one of these once
/// you work through the truth table, so we don't offer redundant modes
/// that would just be a different name for the same behaviour).
/// <see cref="Majority"/> only starts to differ from them once there are
/// 3+ sources. <see cref="ToggleOnAny"/> is the one stateful mode.
/// </para>
/// </summary>
public enum CombineMode
{
    /// <summary>Pressed while at least one source is pressed (logical OR).</summary>
    Any,

    /// <summary>Pressed only while every source is pressed (logical AND).</summary>
    All,

    /// <summary>Pressed only while no source is pressed (logical NOR) — an "idle" trigger.</summary>
    None,

    /// <summary>Pressed only while precisely one source is pressed.</summary>
    ExactlyOne,

    /// <summary>Pressed while more than half of the listed sources are pressed.</summary>
    Majority,

    /// <summary>Each rising edge of the combined sources (none active to any active) flips a persistent on/off flag.</summary>
    ToggleOnAny,

    /// <summary>
    /// Bypasses every mode above; a Lua expression in
    /// <see cref="Models.Rules.MultiSourceCombineRule.ScriptCode"/> decides
    /// the target's state instead. See
    /// <see cref="Scripting.LuaScriptEngine.EvaluateCombine"/> for the
    /// exact contract.
    /// </summary>
    Script,
}
