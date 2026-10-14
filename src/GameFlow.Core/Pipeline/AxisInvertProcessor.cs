using GameFlow.Core.Models;

namespace GameFlow.Core.Pipeline;

/// <summary>
/// Applies a profile's <see cref="AxisInvert"/> to a finished virtual
/// snapshot.
///
/// <para>
/// Runs LAST, after every mapping rule — deliberately, and unlike
/// <see cref="DeviceSettingsProcessor"/>, which runs first. Device
/// conditioning is a correction to the physical reading, so rules should
/// see it; a per-game inversion is a statement about what the game
/// expects to receive, so rules should NOT. Putting it here means a rule
/// that reads the right stick's Y axis still reads the direction the
/// user actually pushed, and someone adding an inversion later doesn't
/// silently reverse the sense of every rule they already wrote.
/// </para>
///
/// <para>
/// Per axis, not radial. <see cref="DeviceSettingsProcessor.ApplyStick"/>
/// has to preserve direction through a magnitude curve, which is why it
/// is careful about the circle. A sign flip is not a curve: mirroring one
/// axis leaves the magnitude untouched, so there is nothing to preserve
/// and inverting X alone is exactly as valid as inverting both.
/// </para>
/// </summary>
public static class AxisInvertProcessor
{
    /// <summary>
    /// Returns <paramref name="snapshot"/> with the axes named by
    /// <paramref name="invert"/> mirrored. Pure, and returns the input
    /// instance untouched when there is nothing to do — the common case,
    /// on a path that runs per slot per tick at up to 1000 Hz.
    /// </summary>
    public static ControllerSnapshot Apply(ControllerSnapshot snapshot, AxisInvert? invert)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (invert is null || invert.IsIdentity)
        {
            return snapshot;
        }

        var left = snapshot.LeftStick;
        var right = snapshot.RightStick;

        if (invert.LeftX is true) { left = left with { X = -left.X }; }
        if (invert.LeftY is true) { left = left with { Y = -left.Y }; }
        if (invert.RightX is true) { right = right with { X = -right.X }; }
        if (invert.RightY is true) { right = right with { Y = -right.Y }; }

        // 1 - v, not -v: a trigger is unsigned 0..1, so mirroring it
        // means reflecting within that range. Same convention as
        // TriggerSettings.Invert and MapSource.Invert.
        var leftTrigger = invert.LeftTrigger is true
            ? 1f - snapshot.LeftTrigger
            : snapshot.LeftTrigger;
        var rightTrigger = invert.RightTrigger is true
            ? 1f - snapshot.RightTrigger
            : snapshot.RightTrigger;

        return snapshot with
        {
            LeftStick = left,
            RightStick = right,
            LeftTrigger = Math.Clamp(leftTrigger, 0f, 1f),
            RightTrigger = Math.Clamp(rightTrigger, 0f, 1f)
        };
    }
}
