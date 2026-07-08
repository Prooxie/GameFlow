namespace GameFlow.Infrastructure.Runtime;

/// <summary>
/// Optional capability for an output sink that receives rumble feedback
/// from the consuming game (the HIDMaestro sink raises this from its
/// <c>OutputReceived</c> callback). Values are normalized 0–1
/// (low-frequency, high-frequency). Forwarding the feedback to the slot's
/// physical device is a follow-up: the previous direct-to-SDL effect path
/// was removed because SDL lightbar/rumble writes hold the SDL joystick
/// lock through blocking Bluetooth HID transfers, which froze the runtime.
/// A safe replacement must marshal writes onto a dedicated effects thread
/// that owns its own device handles.
/// </summary>
public interface IRumbleFeedbackSource
{
    event Action<double, double>? RumbleReceived;
}
