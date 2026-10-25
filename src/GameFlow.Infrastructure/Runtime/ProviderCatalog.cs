namespace GameFlow.Infrastructure.Runtime;

/// <summary>Lists input providers and platform output backends.</summary>
public static class ProviderCatalog
{
    public static IReadOnlyList<ProviderIdentity> KnownProviders =>
    [
        new ProviderIdentity("none",          "No live input",       "Idle input",                   true, "Disables live input and leaves the dashboard idle."),
        new ProviderIdentity("demo",          "DemoInput",           "Animated preview input",       true, "Optional preview source for visual testing and UI iteration."),
        new ProviderIdentity("sdl",           "SDL3 Unified Input",  "Cross-platform input",         true, "The live input provider: SDL3 gamepad mappings + joystick enumeration on all platforms, with Raw Input keyboard/mouse synthesis on Windows."),
        new ProviderIdentity("preview",       "PreviewOutput",       "In-app virtual state preview", true, "Shows the transformed state without creating a native virtual device. Used by disabled output pipelines."),
        new ProviderIdentity("native-gamepad", "Native gamepad", "Linux and macOS virtual HID output", true, "Standard gamepad output through Linux uinput or macOS IOKit. Linux requires /dev/uinput access. macOS requires Apple-approved virtual HID signing. No virtual rumble or Sony effects."),
        new ProviderIdentity("hidmaestro",    "HIDMaestro",          "Windows virtual HID output",   true, "User-mode virtual controller platform (UMDF2 — no kernel driver or reboot). Presents as real hardware to XInput, DirectInput, SDL3 and WGI, with a catalog of Xbox, PlayStation and other gamepad profiles, plus runtime-built custom devices. Needs HIDMaestro 2.0.1 or a later 2.x. Activates when HIDMaestro.Core.dll is placed next to the executable, in a 'HIDMaestro' subfolder, or in the folder named by GAMEFLOW_HIDMAESTRO_DIR, and needs GameFlow to run as Administrator. The sole Windows output backend — there is no fallback if it can't activate, so a slot has no output until it does."),
    ];
}
