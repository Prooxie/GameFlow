using GameFlow.Core.Models;
using GameFlow.Core.Pipeline;

namespace GameFlow.Infrastructure.Runtime.Effects;

/// <summary>
/// Splits one desired controller state across SDL's two output routes.
/// Adaptive triggers and the DualSense's player / mic LEDs require a
/// device-specific effect report; motors and the lightbar stay on SDL's
/// portable APIs even when that report is present. Keeping this policy
/// pure makes the coexistence contract testable without a controller or
/// native SDL calls.
/// </summary>
internal readonly record struct SdlGamepadEffectPlan(
    AdaptiveTriggerSettings? LeftTrigger,
    AdaptiveTriggerSettings? RightTrigger,
    ushort LowFrequencyRumble,
    ushort HighFrequencyRumble,
    EffectColor? LedColor,
    ushort LeftTriggerRumble = 0,
    ushort RightTriggerRumble = 0,
    byte? PlayerLeds = null,
    IndicatorBrightnessLevel? PlayerLedBrightness = null,
    MicLedState? MicLed = null)
{
    public bool HasTriggerReport => LeftTrigger is not null || RightTrigger is not null;

    public bool HasActiveTriggerEffect =>
        IsActive(LeftTrigger) || IsActive(RightTrigger);

    /// <summary>
    /// True when the state asks for lights that only the DualSense report
    /// can reach — the player-indicator row, its brightness, or the mic LED.
    /// </summary>
    public bool HasIndicatorState =>
        PlayerLeds is not null || PlayerLedBrightness is not null || MicLed is not null;

    /// <summary>
    /// A DS5 effect payload is valid only for SDL's PS5 driver. An Off-only
    /// payload is needed solely to release firmware state that this process
    /// previously activated; sending it to every pad can be interpreted as
    /// an unrelated controller's rumble structure.
    /// </summary>
    public bool ShouldSendTriggerReport(
        bool supportsAdaptiveTriggers,
        bool previouslyActivated) =>
        supportsAdaptiveTriggers &&
        HasTriggerReport &&
        (HasActiveTriggerEffect || previouslyActivated);

    /// <summary>
    /// Whether the final DS5 report goes out at all: for triggers (see
    /// <see cref="ShouldSendTriggerReport"/>) or for the indicator LEDs,
    /// which have no portable SDL call. Still PS5-driver only.
    /// </summary>
    public bool ShouldSendDualSenseReport(
        bool supportsAdaptiveTriggers,
        bool triggersPreviouslyActivated) =>
        supportsAdaptiveTriggers &&
        (HasIndicatorState || ShouldSendTriggerReport(supportsAdaptiveTriggers, triggersPreviouslyActivated));

    /// <summary>
    /// Builds the final PS5 device write after SDL's portable rumble and LED
    /// APIs have synchronized their internal state. Keeping this composition
    /// on the plan is a test seam: a trigger-only regression cannot silently
    /// drop the unchanged motor/LED channels again.
    /// </summary>
    /// <param name="destination">At least <see cref="DualSenseEffectEncoder.EffectStateLength"/> bytes.</param>
    /// <param name="rumbleMode">The pad's SDL rumble lane.</param>
    /// <param name="includeTriggers">
    /// False when the report goes out only for the indicator LEDs: a pad
    /// whose triggers this process never armed is left without trigger
    /// sections, exactly as before indicators were sent.
    /// </param>
    public bool TryWriteDualSenseReport(
        Span<byte> destination,
        DualSenseRumbleMode rumbleMode,
        bool includeTriggers = true) =>
        DualSenseEffectEncoder.TryWrite(
            destination,
            includeTriggers ? LeftTrigger : null,
            includeTriggers ? RightTrigger : null,
            LedColor is { } led ? new LightColor(led.R, led.G, led.B) : null,
            LowFrequencyRumble,
            HighFrequencyRumble,
            rumbleMode,
            PlayerLeds,
            PlayerLedBrightness,
            MicLed);

    public static SdlGamepadEffectPlan Create(in ControllerEffectState state) => new(
        ToTriggerSettings(state.LeftTrigger),
        ToTriggerSettings(state.RightTrigger),
        ToRumbleMagnitude(state.LowFrequencyRumble),
        ToRumbleMagnitude(state.HighFrequencyRumble),
        state.LedColor,
        ToRumbleMagnitude(state.LeftTriggerRumble),
        ToRumbleMagnitude(state.RightTriggerRumble),
        state.PlayerLeds,
        state.PlayerLedBrightness,
        state.MicLed);

    private static AdaptiveTriggerSettings? ToTriggerSettings(AdaptiveTriggerCommand? command)
    {
        if (command is not { } value)
        {
            return null;
        }

        return new AdaptiveTriggerSettings
        {
            Mode = value.Effect switch
            {
                AdaptiveTriggerEffect.Off => AdaptiveTriggerMode.Off,
                AdaptiveTriggerEffect.Constant => AdaptiveTriggerMode.Feedback,
                AdaptiveTriggerEffect.Section => AdaptiveTriggerMode.Weapon,
                AdaptiveTriggerEffect.Vibration => AdaptiveTriggerMode.Vibration,
                AdaptiveTriggerEffect.SlopeFeedback => AdaptiveTriggerMode.SlopeFeedback,
                AdaptiveTriggerEffect.MultiplePositionFeedback
                    => AdaptiveTriggerMode.MultiplePositionFeedback,
                AdaptiveTriggerEffect.MultiplePositionVibration
                    => AdaptiveTriggerMode.MultiplePositionVibration,
                _ => AdaptiveTriggerMode.Off,
            },
            StartPosition = value.StartPosition / 255f,
            EndPosition = value.EndPosition / 255f,
            Strength = value.Strength / 255f,
            FrequencyHz = value.FrequencyHz,
        };
    }

    private static ushort ToRumbleMagnitude(double value) =>
        (ushort)Math.Clamp(Math.Round(value * ushort.MaxValue), 0, ushort.MaxValue);

    private static bool IsActive(AdaptiveTriggerSettings? settings) =>
        settings is { Mode: not AdaptiveTriggerMode.Off };
}
