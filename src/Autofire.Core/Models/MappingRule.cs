using System.Text.Json.Serialization;
using Autofire.Core.Enums;
using Autofire.Core.Models.Rules;

namespace Autofire.Core.Models;

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
public abstract record MappingRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Rule";
    public bool Enabled { get; init; } = true;
    public RuleMode Mode { get; init; } = RuleMode.Modify;
}
