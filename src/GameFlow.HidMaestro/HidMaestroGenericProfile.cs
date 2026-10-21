using System.Runtime.Versioning;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using HIDMaestro;

namespace GameFlow.HidMaestro;

/// <summary>
/// Builds the runtime-made "generic DirectInput" profile: a plain HID gamepad
/// whose shape comes from a device template, not from one of the SDK's
/// shipped persona files.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HidMaestroGenericProfile
{
    /// <summary>
    /// The most buttons a generic pad can declare. <c>HMGamepadState.Buttons</c>
    /// is a 32-bit <c>HMButton</c> mask, so a 33rd descriptor button could
    /// never be pressed.
    /// </summary>
    public const int MaxButtons = 32;

    /// <summary>
    /// Builds the profile for <paramref name="shape"/>. Counts outside the
    /// SDK's range are clamped, so a template edited to nonsense still gives
    /// a valid descriptor. A pad always has at least one button.
    /// </summary>
    public static HMProfile Build(string profileId, HidMaestroGenericShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);

        var descriptor = new HidDescriptorBuilder().Gamepad();

        int sticks = Math.Clamp(shape.Thumbsticks, 0, 2);
        if (sticks > 0) { descriptor.AddStick("Left", 16); }
        if (sticks > 1) { descriptor.AddStick("Right", 16); }

        int triggers = Math.Clamp(shape.Triggers, 0, 2);
        if (triggers > 0) { descriptor.AddTrigger("Left", 8); }
        if (triggers > 1) { descriptor.AddTrigger("Right", 8); }

        descriptor.AddButtons(Math.Clamp(shape.Buttons, 1, MaxButtons));
        if (shape.Povs > 0) { descriptor.AddHat(); }

        return new HMProfileBuilder()
            .Id(profileId)
            .Name(shape.ProductString)
            .Vendor("GameFlow")
            .Vid(shape.VendorId)
            .Pid(shape.ProductId)
            .ProductString(shape.ProductString)
            .ManufacturerString("GameFlow")
            .Type("gamepad")
            .Connection("usb")
            .FromDescriptorBuilder(descriptor)
            .Build();
    }
}
