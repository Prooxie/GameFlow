using System.Text.Json.Serialization;
using GameFlow.Core.Enums;

namespace GameFlow.Core.Models.Rules;

/// <summary>
/// "Squeeze a digital trigger like it's analog." While
/// <see cref="ArmButton"/> is held, <see cref="ModulatorStick"/>'s
/// magnitude (0 at rest, 1 fully pushed, any direction — "how hard it
/// presses" reads as push distance, not a specific axis) drives
/// <see cref="TargetTrigger"/> from a feather to full. The output ramps
/// toward that target at <see cref="RampRatePerSecond"/> rather than
/// jumping, so a fast stick flick doesn't spike the trigger instantly.
/// </summary>
public sealed record StickTrimRule : MappingRule
{
    /// <summary>Held to arm the trim. A keyboard key mapped as a button works here too — "a keyboard bumper becomes a trigger you can modulate."</summary>
    [JsonPropertyName("armButton")]
    public ButtonId ArmButton { get; init; } = ButtonId.None;

    [JsonPropertyName("modulatorStick")]
    public StickId ModulatorStick { get; init; } = StickId.Right;

    [JsonPropertyName("targetTrigger")]
    public TriggerId TargetTrigger { get; init; } = TriggerId.Right;

    /// <summary>Stick magnitude below this reads as zero press, so a centered stick doesn't feather the trigger from drift.</summary>
    [JsonPropertyName("deadzone")]
    public float Deadzone { get; init; } = 0.08f;

    /// <summary>
    /// How fast the output chases the stick's (deadzone-adjusted)
    /// magnitude, in trigger-units per second. 4.0 (default) sweeps
    /// 0→1 in 250 ms. Higher = snappier/more immediate; lower = softer,
    /// more gradual squeeze.
    /// </summary>
    [JsonPropertyName("rampRatePerSecond")]
    public float RampRatePerSecond { get; init; } = 4.0f;

    /// <summary>
    /// True (default): releasing ArmButton snaps the trigger to 0
    /// immediately. False: the trigger FREEZES at its last value when
    /// released, and only moves again once re-armed — useful for a
    /// "set and forget" constant partial press.
    /// </summary>
    [JsonPropertyName("resetOnRelease")]
    public bool ResetOnRelease { get; init; } = true;

    /// <summary>When true, the modulator stick is zeroed in the virtual output while armed, so it doesn't also drive a normal stick mapping at the same time.</summary>
    [JsonPropertyName("suppressModulatorStick")]
    public bool SuppressModulatorStick { get; init; }
}
