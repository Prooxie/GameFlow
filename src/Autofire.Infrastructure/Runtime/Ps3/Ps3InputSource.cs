using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime.Ps3;

/// <summary>
/// Input source for the original DualShock 3 (PS3) controller.
///
/// <para><b>Status:</b> scaffold only. Two backends are viable:</para>
/// <list type="bullet">
/// <item><b>DsHidMini</b> (recommended, modern). Nefarius's
/// open-source filter driver that re-exposes a paired DS3 as either
/// a generic HID device or an XInput pad. The latter mode means
/// Autofire can pick it up via the existing
/// <c>XInputInputSource</c> path with no extra code, so this
/// scaffold mainly exists to surface the option in the dropdown
/// and document the requirement.</item>
/// <item><b>libusb / WinUSB</b> direct. Talk to the DS3 over its
/// USB HID interrupt endpoint, parse the 49-byte report layout, and
/// build a <c>ControllerSnapshot</c>. Required only if the user
/// can't or won't install DsHidMini.</item>
/// </list>
///
/// <para>
/// Implementation pointers: when implementing the libusb path, the
/// DS3 needs an out-of-band <c>SET_FEATURE</c> "magic packet" sent
/// before it starts streaming reports — see Sony's HID spec or
/// any of the open-source DS3 reverse-engineering projects.
/// </para>
/// </summary>
public sealed class Ps3InputSource : ScaffoldedInputSourceBase
{
    public Ps3InputSource(
        InputDeviceCatalog inputDeviceCatalog,
        ILogger<Ps3InputSource> logger)
        : base(
            inputDeviceCatalog,
            logger,
            "DualShock 3 (PS3)",
            "Requires DsHidMini driver or libusb integration — not yet operational. Falls back to no-op.")
    {
    }
}
