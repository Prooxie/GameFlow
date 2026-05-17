using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime.VJoy;

/// <summary>
/// Output sink that emits a virtual joystick through the vJoy device
/// driver. Unlike ViGEm — which emulates a specific Xbox/PlayStation
/// pad — vJoy presents a configurable HID joystick (up to 8 axes,
/// 128 buttons, 4 hats) that legacy DirectInput games and many
/// flight/simulation titles can consume.
///
/// <para><b>Status:</b> scaffold only. Implementation needs:</para>
/// <list type="bullet">
/// <item><c>vJoyInterface.dll</c> in the application directory (the
/// 32/64-bit variant matching the host process must match the
/// installed vJoy driver bitness).</item>
/// <item>PInvoke entry points: <c>AcquireVJD</c> /
/// <c>GetVJDStatus</c> / <c>UpdateVJD</c> / <c>RelinquishVJD</c>.</item>
/// <item>Mapping our
/// <see cref="Autofire.Core.Models.ControllerSnapshot"/> shape onto
/// vJoy's <c>JOYSTICK_POSITION_V2</c> structure — pay attention to
/// stick axis sign and the 0..32767 range vs our -1.0..+1.0 floats.</item>
/// <item>Choosing a vJoy device index (1..16). Either expose this
/// in the profile JSON or auto-pick the first free index.</item>
/// </list>
///
/// <para>
/// Implementation pointers: vJoy v2.1.9 is the de-facto stable
/// release; v2.2 betas exist but have driver-signing issues on
/// recent Windows builds. The vJoy SDK ships an example C# wrapper
/// in <c>SDK/c#/</c>.
/// </para>
/// </summary>
public sealed class VJoyOutputSink : ScaffoldedOutputSinkBase
{
    public VJoyOutputSink(ILogger<VJoyOutputSink> logger)
        : base(
            logger,
            "vJoy virtual joystick",
            "Requires vJoy device driver (vJoyInterface.dll) — not yet operational. Falls back to no-op.")
    {
    }
}
