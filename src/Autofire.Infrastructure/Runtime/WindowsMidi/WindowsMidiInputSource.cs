using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime.WindowsMidi;

/// <summary>
/// Input source that translates incoming MIDI events from a
/// connected MIDI device (controller, keyboard, foot-switch, etc.)
/// into virtual gamepad inputs. Uses Windows MIDI Services (the
/// modern Windows 11+ replacement for the legacy
/// <c>winmm</c> MIDI APIs).
///
/// <para><b>Status:</b> scaffold only. Implementation needs:</para>
/// <list type="bullet">
/// <item>A user-defined mapping table: which MIDI message
/// (channel, note/CC number, value range) maps to which virtual
/// button or stick axis. This belongs in the profile JSON so it
/// can be edited per-profile.</item>
/// <item>A subscriber on
/// <c>Microsoft.Windows.Devices.Midi2.MidiSession</c> input
/// endpoints that pushes events into a per-tick aggregator.</item>
/// <item>Velocity / CC-value scaling onto the
/// <see cref="Autofire.Core.Models.ControllerSnapshot"/> stick axis
/// range (-1.0 .. 1.0).</item>
/// </list>
///
/// <para>
/// Implementation pointers: the
/// <c>Microsoft.Windows.Devices.Midi2</c> WinRT API is the
/// recommended surface. NuGet package
/// <c>Microsoft.Windows.Devices.Midi2</c> exposes it for unpackaged
/// .NET apps; running on Windows 10 needs the legacy
/// <c>winmm.dll</c> fallback.
/// </para>
/// </summary>
public sealed class WindowsMidiInputSource : ScaffoldedInputSourceBase
{
    public WindowsMidiInputSource(
        InputDeviceCatalog inputDeviceCatalog,
        ILogger<WindowsMidiInputSource> logger)
        : base(
            inputDeviceCatalog,
            logger,
            "Windows MIDI input",
            "Requires Windows MIDI Services and a profile-defined mapping — not yet operational. Falls back to no-op.")
    {
    }
}
