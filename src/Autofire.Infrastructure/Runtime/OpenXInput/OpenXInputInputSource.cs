using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime.OpenXInput;

/// <summary>
/// Input source backed by OpenXInput — a drop-in DLL replacement for
/// Microsoft's XInput that supports more than 4 simultaneous
/// controllers and exposes additional non-XInput devices through the
/// same XInput-shape API.
///
/// <para><b>Status:</b> scaffold only. Implementation strategy:</para>
/// <list type="bullet">
/// <item>Most of <c>XInputInputSource</c>'s codepath can be reused
/// — OpenXInput is binary-compatible with XInput's exported entry
/// points (<c>XInputGetState</c>, <c>XInputGetCapabilities</c>,
/// etc.).</item>
/// <item>The only change is which DLL the PInvoke binds against:
/// load <c>OpenXinput1_4.dll</c> from the app folder (or wherever
/// the user dropped it) instead of the system XInput.</item>
/// <item>Bump the per-tick slot scan from 4 to 16 — OpenXInput's
/// max controller count.</item>
/// </list>
///
/// <para>
/// Implementation pointers: see <c>XInput/XInputInterop.cs</c> for
/// the existing PInvoke layer. A clean approach is to extract the
/// PInvoke surface into an interface and provide an OpenXInput
/// implementation that points at the alternate library.
/// </para>
/// </summary>
public sealed class OpenXInputInputSource : ScaffoldedInputSourceBase
{
    public OpenXInputInputSource(
        InputDeviceCatalog inputDeviceCatalog,
        ILogger<OpenXInputInputSource> logger)
        : base(
            inputDeviceCatalog,
            logger,
            "OpenXInput (XInput-compatible)",
            "Requires OpenXinput1_4.dll alongside the application — not yet operational. Falls back to no-op.")
    {
    }
}
