namespace GameFlow.Infrastructure.Runtime;

/// <summary>
/// The set of providers this build actually ships. Input is SDL3 unified
/// (gamepads everywhere; keyboard/mouse arrive via Windows Raw Input and
/// are synthesized into gamepad snapshots), with <c>demo</c>/<c>none</c>
/// for UI iteration and idling.
///
/// <para>Output on Windows is HIDMaestro and ONLY HIDMaestro — the
/// user-mode virtual controller platform (no kernel driver, no reboot).
/// <c>preview</c> remains in the catalog solely as the non-Windows
/// fallback (HIDMaestro is Windows-only) and never appears as a Windows
/// choice; see <see cref="OutputProviderPolicy"/>, which resolves every
/// requested id to the platform's single real backend. ViGEm Bus was
/// retired as a dependency in favor of HIDMaestro exclusively; any
/// profile still referencing a <c>vigem-*</c> provider id migrates to
/// <c>hidmaestro</c> automatically. Legacy providers from further back
/// (XInput, OpenXInput, x360ce, PS3, GameInput, vJoy, Windows MIDI) were
/// retired earlier and migrate the same way.</para>
/// </summary>
public static class ProviderCatalog
{
    public static IReadOnlyList<ProviderIdentity> KnownProviders =>
    [
        new ProviderIdentity("none",          "No live input",       "Idle input",                   true, "Disables live input and leaves the dashboard idle."),
        new ProviderIdentity("demo",          "DemoInput",           "Animated preview input",       true, "Optional preview source for visual testing and UI iteration."),
        new ProviderIdentity("sdl",           "SDL3 Unified Input",  "Cross-platform input",         true, "The live input provider: SDL3 gamepad mappings + joystick enumeration on all platforms, with Raw Input keyboard/mouse synthesis on Windows."),
        new ProviderIdentity("preview",       "PreviewOutput",       "In-app virtual state preview", true, "Non-Windows fallback output: shows the transformed state without creating a native virtual device. Never offered on Windows, where HIDMaestro is the sole backend."),
        new ProviderIdentity("hidmaestro",    "HIDMaestro",          "Windows virtual HID output",   true, "User-mode virtual controller platform (UMDF2 — no kernel driver or reboot). Presents as real hardware to XInput, DirectInput, SDL3 and WGI, with a 225-profile catalog covering Xbox, PlayStation, wheels, HOTAS and more, plus runtime-built custom devices. Activates when HIDMaestro.Core.dll is placed next to the executable (or in a 'HIDMaestro' subfolder), and needs GameFlow to run as Administrator. The sole Windows output backend — there is no fallback if it can't activate, so a slot has no output until it does."),
    ];
}
