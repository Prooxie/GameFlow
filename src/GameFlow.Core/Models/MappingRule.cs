using System.Text.Json.Serialization;
using GameFlow.Core.Enums;
using GameFlow.Core.Models.Rules;

namespace GameFlow.Core.Models;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(StickThresholdRule), "stick-threshold")]
[JsonDerivedType(typeof(StickAutofireRule), "stick-autofire")]
[JsonDerivedType(typeof(FreezeLastDirectionRule), "freeze-last-direction")]
[JsonDerivedType(typeof(ButtonRemapRule), "button-remap")]
[JsonDerivedType(typeof(ButtonAutofireRule), "button-autofire")]
[JsonDerivedType(typeof(ButtonComboRule), "button-combo")]
[JsonDerivedType(typeof(MultiButtonAutofireRule), "multi-button-autofire")]
[JsonDerivedType(typeof(RuleToggleRule), "rule-toggle")]
[JsonDerivedType(typeof(ControlScriptRule), "control-script")]
[JsonDerivedType(typeof(SocdCleanRule), "socd-clean")]
[JsonDerivedType(typeof(StickTrimRule), "stick-trim")]
[JsonDerivedType(typeof(MultiSourceMapRule), "multi-source-map")]
[JsonDerivedType(typeof(TouchpadMapRule), "touchpad-map")]
[JsonDerivedType(typeof(GyroMapRule), "gyro-map")]
[JsonDerivedType(typeof(AxisInvertRule), "axis-invert")]
public abstract record MappingRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Rule";
    public bool Enabled { get; init; } = true;
    public RuleMode Mode { get; init; } = RuleMode.Modify;

    /// <summary>
    /// Empty (default) = Base — active unless the active layer rebinds
    /// its input. A non-empty value ties this rule to a
    /// <see cref="ShiftLayer.Id"/>: it applies only while
    /// <see cref="Pipeline.ShiftLayerResolver.ActiveLayerId"/> equals it.
    /// See <see cref="ShiftLayer"/> for how a layer overrides Base.
    /// </summary>
    public string LayerId { get; init; } = string.Empty;
}
