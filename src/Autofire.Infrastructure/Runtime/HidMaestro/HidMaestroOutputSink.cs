using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// Output sink that emits a fully-custom HID device through HidMaestro
/// (a generic HID-emulation driver, similar in spirit to vJoy but
/// with arbitrary report-descriptor authoring rather than a fixed
/// joystick-shaped descriptor). Useful when the target application
/// expects a non-standard HID layout — racing wheels, fight sticks
/// with extra paddles, light-gun adapters, etc.
///
/// <para><b>Status:</b> scaffold only. Implementation needs:</para>
/// <list type="bullet">
/// <item>The HidMaestro driver installed and a virtual device
/// instance created (typically through the HidMaestro control
/// applet).</item>
/// <item>An authored HID report descriptor matching the device the
/// user wants to expose. Profiles should be able to point at a
/// descriptor file by name.</item>
/// <item>Translation from
/// <see cref="Autofire.Core.Models.ControllerSnapshot"/> to the
/// descriptor's report layout. This is descriptor-specific — there's
/// no one-size-fits-all mapping like vJoy provides.</item>
/// </list>
///
/// <para>
/// Implementation pointers: HidMaestro exposes a Win32 file-handle
/// API. Open <c>\\.\HidMaestroN</c> (where N is the device index)
/// with <c>CreateFile</c>, then <c>WriteFile</c> the HID report
/// payload at every tick. The first byte is the report ID; the
/// remaining bytes follow the descriptor.
/// </para>
/// </summary>
public sealed class HidMaestroOutputSink : ScaffoldedOutputSinkBase
{
    public HidMaestroOutputSink(ILogger<HidMaestroOutputSink> logger)
        : base(
            logger,
            "HidMaestro virtual HID",
            "Requires HidMaestro driver and a profile-defined HID descriptor — not yet operational. Falls back to no-op.")
    {
    }
}
