using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime.WindowsMidi;

/// <summary>
/// Output sink that emits MIDI events derived from virtual gamepad
/// state. The inverse direction of <see cref="WindowsMidiInputSource"/>:
/// instead of consuming MIDI as input, this surfaces button presses
/// and stick movements as MIDI Note-On / Note-Off / CC messages on
/// a configurable output endpoint.
///
/// <para>
/// Useful for music-software workflows (Ableton, FL Studio, etc.)
/// where the user wants to use a gamepad as a controller surface,
/// or for hardware MIDI rigs where Autofire lives between physical
/// controllers and outboard gear.
/// </para>
///
/// <para><b>Status:</b> scaffold only. Implementation needs:</para>
/// <list type="bullet">
/// <item>Output endpoint selection: name-based pick from
/// <c>MidiEndpointDeviceWatcher</c>'s output endpoints.</item>
/// <item>A user-defined mapping table (lives in the profile JSON):
/// gamepad-control → MIDI message template.</item>
/// <item>Edge detection so that a held button doesn't spam Note-On
/// at every tick — only the rising edge fires Note-On, the falling
/// edge fires Note-Off.</item>
/// <item>Sticks should map to CC values (or pitch-bend) with
/// configurable scaling and deadzones.</item>
/// </list>
///
/// <para>
/// Implementation pointers: same package as the input source
/// (<c>Microsoft.Windows.Devices.Midi2</c>). Use
/// <c>MidiSession.CreateEndpointConnection</c> to acquire an output
/// connection, then <c>SendSingleMessageStruct</c> per MIDI event.
/// </para>
/// </summary>
public sealed class WindowsMidiOutputSink : ScaffoldedOutputSinkBase
{
    public WindowsMidiOutputSink(ILogger<WindowsMidiOutputSink> logger)
        : base(
            logger,
            "Windows MIDI output",
            "Requires Windows MIDI Services and a profile-defined mapping — not yet operational. Falls back to no-op.")
    {
    }
}
