namespace Autofire.Infrastructure.Runtime;

/// <summary>
/// The set of providers this build actually ships. Input is SDL3 unified
/// (gamepads everywhere; keyboard/mouse arrive via Windows Raw Input and
/// are synthesized into gamepad snapshots), with <c>demo</c>/<c>none</c>
/// for UI iteration and idling. Output is ViGEm Bus (Xbox 360 /
/// DualShock 4 / DualSense) plus HIDMaestro, the user-mode virtual
/// controller platform that replaces the kernel-driver stack on Windows.
/// Legacy providers (XInput, OpenXInput, x360ce, PS3, GameInput, vJoy,
/// Windows MIDI) were retired; their ids migrate to SDL / preview.
/// </summary>
public static class ProviderCatalog
{
    public static IReadOnlyList<ProviderIdentity> KnownProviders =>
    [
        new ProviderIdentity("none",          "No live input",       "Idle input",                   true, "Disables live input and leaves the dashboard idle."),
        new ProviderIdentity("demo",          "DemoInput",           "Animated preview input",       true, "Optional preview source for visual testing and UI iteration."),
        new ProviderIdentity("sdl",           "SDL3 Unified Input",  "Cross-platform input",         true, "The live input provider: SDL3 gamepad mappings + joystick enumeration on all platforms, with Raw Input keyboard/mouse synthesis on Windows."),
        new ProviderIdentity("preview",       "PreviewOutput",       "In-app virtual state preview", true, "Shows the transformed state without creating a native virtual device."),
        new ProviderIdentity("vigem-xbox360", "ViGEm Xbox 360",      "Windows virtual Xbox output",  true, "Virtual Xbox 360 controller via ViGEm Bus. Requires the ViGEm Bus driver (Windows)."),
        new ProviderIdentity("vigem-ds4",     "ViGEm DualShock 4",   "Windows virtual DS4 output",   true, "Virtual DualShock 4 controller via ViGEm Bus. Requires the ViGEm Bus driver (Windows)."),
        new ProviderIdentity("vigem-ds5",     "ViGEm DualSense",     "Windows virtual DS5 output",   true, "Virtual DualSense controller via ViGEm Bus (emitted as a DS4-shaped device on the bus). Requires the ViGEm Bus driver (Windows)."),
        new ProviderIdentity("hidmaestro",    "HIDMaestro",          "Windows virtual HID output",   true, "User-mode virtual controller platform (UMDF2 — no kernel driver or reboot). Presents as real hardware to XInput, DirectInput, SDL3 and WGI. Active in builds compiled with the HIDMaestro SDK."),
    ];
}
