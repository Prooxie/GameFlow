using GameFlow.Core.Pipeline;

namespace GameFlow.Infrastructure.Runtime.Effects;

/// <summary>
/// The light changes ONE output report from the game asked for, decoded
/// from what it wrote to the virtual DualSense or DualShock 4.
///
/// <para>
/// Every member is optional because the report's validity flags authorise
/// each section separately: a report that sets the player LEDs carries a
/// lightbar colour too, but that colour is padding unless its own flag is
/// set, and treating padding as a request would blank the bar. Null means
/// "this report did not ask", never "turn it off".
/// </para>
/// </summary>
/// <param name="Color">Lightbar colour, when authorised.</param>
/// <param name="FlashOn">DualShock 4 flash lit time, in the firmware's 10 ms units.</param>
/// <param name="FlashOff">DualShock 4 flash dark time, in 10 ms units.</param>
/// <param name="PlayerIndicator">DualSense player-LED bits (low five bits).</param>
/// <param name="MicLed">DualSense mute LED: 0 off, 1 on, 2 pulse.</param>
/// <param name="LedBrightness">DualSense player-LED brightness step: 0 bright, 1 medium, 2 dim.</param>
public sealed record GameLighting(
    LightColor? Color = null,
    byte? FlashOn = null,
    byte? FlashOff = null,
    byte? PlayerIndicator = null,
    byte? MicLed = null,
    byte? LedBrightness = null)
{
    /// <summary>True when the report authorised at least one light.</summary>
    public bool HasAny =>
        Color is not null || FlashOn is not null || FlashOff is not null
        || PlayerIndicator is not null || MicLed is not null || LedBrightness is not null;
}

/// <summary>
/// Optional capability for an output sink whose virtual pad receives
/// lighting from the game (the HIDMaestro sink raises it from the decoded
/// Sony output reports). Kept beside <see cref="IRumbleFeedbackSource"/>
/// rather than folded into it so a sink that only knows rumble needs no
/// change.
/// </summary>
public interface ILightingFeedbackSource
{
    event Action<GameLighting>? LightingReceived;
}
