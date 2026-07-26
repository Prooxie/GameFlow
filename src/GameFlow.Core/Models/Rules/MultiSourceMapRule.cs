using System.Text.Json.Serialization;
using GameFlow.Core.Enums;

namespace GameFlow.Core.Models.Rules;

/// <summary>
/// One mapping ROW: many input sources combined into ONE output target.
/// Each source reads as a float — a button is 0/1, a trigger 0..1, a
/// stick axis -1..1, a stick magnitude 0..1 — the sources are combined
/// per <see cref="CombineMode"/>, and the result is written to the
/// target (a button via <see cref="PressThreshold"/>, a stick axis, or
/// a trigger).
///
/// <para>
/// Two rows targeting the same output apply in rule order,
/// last-write-wins — the same convention as every other rule
/// interaction in this pipeline.
/// </para>
///
/// <para><b>Interpretation note:</b> the source spec says "6 combine
/// modes + formula" without naming the six. The six implemented here —
/// Maximum, Minimum, Sum, Average, Multiply, FirstActive — are this
/// implementation's selection of the canonical set for this domain,
/// flagged rather than presented as the spec's own list.</para>
/// </summary>
public sealed record MultiSourceMapRule : MappingRule
{
    [JsonPropertyName("sources")]
    public IReadOnlyList<MapSource> Sources { get; init; } = [];

    [JsonPropertyName("combineMode")]
    public CombineMode CombineMode { get; init; } = CombineMode.Maximum;

    /// <summary>Used only when <see cref="CombineMode"/> is <see cref="CombineMode.Formula"/> — see <see cref="Formulas.FormulaExpression"/> for the language.</summary>
    [JsonPropertyName("formula")]
    public string Formula { get; init; } = string.Empty;

    [JsonPropertyName("targetKind")]
    public MapTargetKind TargetKind { get; init; } = MapTargetKind.Button;

    [JsonPropertyName("targetButton")]
    public ButtonId TargetButton { get; init; } = ButtonId.None;

    [JsonPropertyName("targetStick")]
    public StickId TargetStick { get; init; } = StickId.Left;

    [JsonPropertyName("targetTrigger")]
    public TriggerId TargetTrigger { get; init; } = TriggerId.Left;

    /// <summary>Button targets only: the combined value at/above which the button reports pressed.</summary>
    [JsonPropertyName("pressThreshold")]
    public float PressThreshold { get; init; } = 0.5f;

    /// <summary>When true, every source's own contribution is zeroed in the virtual output (button → released, axis → 0, trigger → 0) so only the combined target carries it.</summary>
    [JsonPropertyName("suppressSources")]
    public bool SuppressSources { get; init; }
}

/// <summary>One input source in a <see cref="MultiSourceMapRule"/> row.</summary>
public sealed record MapSource
{
    [JsonPropertyName("kind")]
    public MapSourceKind Kind { get; init; } = MapSourceKind.Button;

    [JsonPropertyName("button")]
    public ButtonId Button { get; init; } = ButtonId.None;

    [JsonPropertyName("stick")]
    public StickId Stick { get; init; } = StickId.Left;

    [JsonPropertyName("trigger")]
    public TriggerId Trigger { get; init; } = TriggerId.Left;

    /// <summary>Flips the read value: 1-v for unsigned sources (button/trigger/magnitude), -v for signed stick axes.</summary>
    [JsonPropertyName("invert")]
    public bool Invert { get; init; }
}

/// <summary>What a <see cref="MapSource"/> reads.</summary>
public enum MapSourceKind
{
    Button,
    StickAxisX,
    StickAxisY,
    StickMagnitude,
    Trigger,

    /// <summary>
    /// Raw angular velocity in radians/second — see
    /// <see cref="ControllerSnapshot.GyroPitch"/>. Unlike the other
    /// source kinds these are NOT pre-normalized to a 0..1 or -1..1
    /// range, so a formula using them usually wants an explicit scale
    /// (e.g. <c>clamp(s1 / 4, -1, 1)</c>). Kept raw deliberately: the
    /// alternative is baking in a normalization constant that would be
    /// wrong for anyone whose pad or grip differs.
    /// </summary>
    GyroPitch,
    GyroYaw,
    GyroRoll
}

/// <summary>What a <see cref="MultiSourceMapRule"/> writes.</summary>
public enum MapTargetKind
{
    Button,
    StickAxisX,
    StickAxisY,
    Trigger
}

/// <summary>How a row's sources fold into one value — see the interpretation note on <see cref="MultiSourceMapRule"/>.</summary>
public enum CombineMode
{
    /// <summary>Largest value wins — "any of these" for buttons, strongest push for analog.</summary>
    Maximum,

    /// <summary>Smallest value wins — "all of these" for buttons.</summary>
    Minimum,

    /// <summary>Values add up (button target still gates through <see cref="MultiSourceMapRule.PressThreshold"/>; axis/trigger targets clamp on write).</summary>
    Sum,

    /// <summary>Arithmetic mean of the sources.</summary>
    Average,

    /// <summary>Values multiply — natural gating (a held button scales an analog source by 1, a released one zeroes it).</summary>
    Multiply,

    /// <summary>The first source (in list order) past a small activity epsilon wins outright; priority ordering.</summary>
    FirstActive,

    /// <summary>The row's <see cref="MultiSourceMapRule.Formula"/> decides — see <see cref="Formulas.FormulaExpression"/>.</summary>
    Formula
}
