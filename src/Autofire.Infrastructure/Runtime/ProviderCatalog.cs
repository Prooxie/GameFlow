namespace Autofire.Infrastructure.Runtime;

public static class ProviderCatalog
{
    public static IReadOnlyList<ProviderIdentity> KnownProviders =>
    [
        new ProviderIdentity("none",         "No live input",          "Idle input",                   true,  "Disables live input and leaves the dashboard idle."),
        new ProviderIdentity("demo",         "DemoInput",              "Animated preview input",       true,  "Optional preview source for visual testing and UI iteration."),
        new ProviderIdentity("xinput",       "XInput",                 "Windows XInput input",         true,  "Native Windows XInput driver. Supports up to 4 Xbox-compatible controllers simultaneously."),
        new ProviderIdentity("sdl",          "SDL3 Unified Input",     "Cross-platform input",         false, "SDL3 gamepad mappings plus joystick enumeration for all platforms. Pending interop stabilisation."),
        new ProviderIdentity("gameinput",    "Microsoft.GameInput",    "Windows experimental input",   false, "Experimental native Windows provider. Keep disabled until the interop layer is isolated out-of-process."),
        new ProviderIdentity("preview",      "PreviewOutput",          "In-app virtual state preview", true,  "Shows the transformed state without creating a native virtual device."),
        new ProviderIdentity("vigem-xbox360","ViGEm Xbox 360",         "Windows virtual Xbox output",  true,  "Virtual Xbox 360 controller via ViGEm Bus. Requires ViGEm Bus driver installed."),
        new ProviderIdentity("vigem-ds4",    "ViGEm DualShock 4",      "Windows virtual DS4 output",   true,  "Virtual DualShock 4 controller via ViGEm Bus. Requires ViGEm Bus driver installed."),
        new ProviderIdentity("uinput",       "Linux uinput",           "Linux virtual output",         false, "Planned native virtual device provider for Linux."),
        new ProviderIdentity("corehid",      "macOS CoreHID",          "macOS virtual output",         false, "Planned native virtual device provider for macOS."),
    ];
}
