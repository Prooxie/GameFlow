using GameFlow.Infrastructure.Runtime;

namespace GameFlow.Infrastructure.Runtime.Input.Mac;

/// <summary>
/// macOS counterpart to RawInputDeviceScanner / LinuxInputDeviceScanner
/// — but unlike either of those, there is nothing to enumerate: CGEventTap
/// reports one aggregate stream for every keyboard/mouse system-wide
/// (see MacRawInputReader's own doc comment). This publishes two fixed
/// rows so the device picker has SOMETHING to select, both of which map
/// to the same aggregate reader regardless of which one is chosen —
/// an honest reflection of the real capability, not a simulation of
/// per-device selection this API can't actually provide.
/// </summary>
internal static class MacInputDeviceScanner
{
    internal const string AggregateKeyboardId = "mac-keyboard-aggregate";
    internal const string AggregateMouseId = "mac-mouse-aggregate";

    public static IReadOnlyList<InputDeviceInfo> Scan()
    {
        return
        [
            new InputDeviceInfo(AggregateKeyboardId, "Keyboard (all — no per-device on macOS)", Category: DeviceCategory.Keyboard),
            new InputDeviceInfo(AggregateMouseId, "Mouse (all — no per-device on macOS)", Category: DeviceCategory.Mouse),
        ];
    }
}
