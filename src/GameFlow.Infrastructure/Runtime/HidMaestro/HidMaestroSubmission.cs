using GameFlow.Core.Enums;
using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>The eight positions of a hat switch, plus released.</summary>
public enum HatDirection : byte
{
    None,
    North,
    NorthEast,
    East,
    SouthEast,
    South,
    SouthWest,
    West,
    NorthWest,
}

/// <summary>
/// One frame of motion, already converted to the SDK's units.
/// <see cref="Present"/> is false on a pad with no sensor, which is
/// what keeps "held perfectly still" distinguishable from "no gyro
/// here" — writing zeroes for the latter would claim a reading the
/// hardware never produced.
/// </summary>
/// <param name="Present">False on a pad with no motion sensor; nothing is written.</param>
/// <param name="AccelG">Acceleration in g, SDL sensor frame.</param>
/// <param name="GyroDps">Angular velocity in degrees per second, same frame.</param>
internal readonly record struct MotionSubmission(
    bool Present,
    (float X, float Y, float Z) AccelG,
    (float X, float Y, float Z) GyroDps)
{
    public static readonly MotionSubmission None = new(false, default, default);
}

/// <summary>
/// One frame of the touch surface, in the Sony native ranges
/// (X 0..1919, Y 0..1079) the SDK documents.
/// </summary>
internal readonly record struct TouchSubmission(
    bool Finger0Down, ushort Finger0X, ushort Finger0Y, byte Finger0Id,
    bool Finger1Down, ushort Finger1X, ushort Finger1Y, byte Finger1Id)
{
    public static readonly TouchSubmission None = default;
}

/// <summary>
/// The unit conversions between a <see cref="ControllerSnapshot"/> and the
/// SDK's input ranges. The output sink and the typed adapter share
/// them, so the two cannot drift apart.
/// </summary>
internal static class HidMaestroSubmission
{
    /// <summary>Degrees per radian — the snapshot carries SDL's rad/s, the SDK wants deg/s.</summary>
    private const float DegreesPerRadian = 57.29577951308232f;

    /// <summary>Standard gravity in m/s² — the snapshot carries SDL's m/s², the SDK wants g.</summary>
    private const float StandardGravity = 9.80665f;

    /// <summary>Full charge, as a percentage.</summary>
    internal const byte FullCharge = 100;

    /// <summary>
    /// Maps a stick axis from the snapshot's [-1, 1] onto the SDK's [0, 1].
    /// A non-finite value becomes NaN, which the SDK writes as the axis
    /// rest position instead of a corrupt reading.
    /// </summary>
    public static float ToAxis(float signed) =>
        float.IsFinite(signed) ? (Math.Clamp(signed, -1f, 1f) + 1f) * 0.5f : float.NaN;

    /// <summary>Clamps a trigger to [0, 1]. A non-finite value becomes NaN, as in <see cref="ToAxis"/>.</summary>
    public static float ToTrigger(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0f, 1f) : float.NaN;

    /// <summary>Resolves the four d-pad buttons into one hat direction. A diagonal wins over a single direction.</summary>
    public static HatDirection ResolveHat(ControllerSnapshot s)
    {
        bool up = s.IsPressed(ButtonId.DpadUp);
        bool down = s.IsPressed(ButtonId.DpadDown);
        bool left = s.IsPressed(ButtonId.DpadLeft);
        bool right = s.IsPressed(ButtonId.DpadRight);
        if (up && right) return HatDirection.NorthEast;
        if (down && right) return HatDirection.SouthEast;
        if (down && left) return HatDirection.SouthWest;
        if (up && left) return HatDirection.NorthWest;
        if (up) return HatDirection.North;
        if (right) return HatDirection.East;
        if (down) return HatDirection.South;
        if (left) return HatDirection.West;
        return HatDirection.None;
    }

    /// <summary>
    /// Charge to report on the emitted pad, on the SDK's 0..10 scale.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These fields were never written at all to begin with, and an
    /// unwritten one is not an absent one: a DualSense or DualShock 4
    /// report always carries a battery, so it went out as zero and every
    /// virtual pad announced itself as nearly flat, forever, whatever was
    /// driving it — a wired pad, a keyboard, or nothing.
    /// </para>
    /// <para>
    /// Writing a percentage was the second half of the same mistake. The
    /// SDK documents the field as "Battery capacity, 0..10 (Sony firmware
    /// convention)", and this was read as meaning the SDK's own property
    /// carries that scale. It does not — it takes a percentage, and
    /// down-scales into the Sony field itself. Rescaling here divided the
    /// charge by ten a second time.
    /// </para>
    /// <para>
    /// Settled by measurement rather than by the wording. With the
    /// division in place a source pad at 25% was submitted as 3 and read
    /// back as 3%, and a source at 65% as 7 and read back as roughly 7% —
    /// consumers take the value as a percentage, exactly and without
    /// rescaling. Two independent readings, both matching the raw number
    /// rather than a tenth of it, which is not a coincidence available to
    /// the 0..10 reading.
    /// </para>
    /// <para>
    /// A source with no battery maps to full, which is what a wired
    /// controller says about itself — the honest answer as well as the
    /// quiet one, since there is no battery here to be low.
    /// </para>
    /// </remarks>
    public static byte BatteryLevelFor(ControllerSnapshot s) =>
        s.BatteryPercent is { } percent
            ? (byte)Math.Clamp(percent, 0, FullCharge)
            : FullCharge;

    /// <summary>The unit conversion alone, split out so it is testable without a deployed SDK.</summary>
    public static MotionSubmission ConvertMotion(ControllerSnapshot s)
    {
        if (!s.HasGyro
            || !float.IsFinite(s.AccelX) || !float.IsFinite(s.AccelY) || !float.IsFinite(s.AccelZ)
            || !float.IsFinite(s.GyroPitch * DegreesPerRadian)
            || !float.IsFinite(s.GyroYaw * DegreesPerRadian)
            || !float.IsFinite(s.GyroRoll * DegreesPerRadian))
        {
            return MotionSubmission.None;
        }

        return new MotionSubmission(
            Present: true,
            AccelG: (s.AccelX / StandardGravity, s.AccelY / StandardGravity, s.AccelZ / StandardGravity),
            GyroDps: (s.GyroPitch * DegreesPerRadian, s.GyroYaw * DegreesPerRadian, s.GyroRoll * DegreesPerRadian));
    }

    /// <summary>The coordinate conversion alone, split out so it is testable without a deployed SDK.</summary>
    public static TouchSubmission ConvertTouch(ControllerSnapshot s)
    {
        var contacts = s.TouchContacts;
        if (contacts.Count == 0)
        {
            return s.TouchDown
                ? new TouchSubmission(
                    true, TouchX(s.TouchX), TouchY(s.TouchY), 0,
                    false, 0, 0, 0)
                : TouchSubmission.None;
        }

        var first = contacts[0];
        var hasSecond = contacts.Count > 1;
        var second = hasSecond ? contacts[1] : default;

        return new TouchSubmission(
            true, TouchX(first.X), TouchY(first.Y), (byte)(first.FingerIndex & 0x7F),
            hasSecond, TouchX(second.X), TouchY(second.Y), (byte)(second.FingerIndex & 0x7F));
    }

    /// <summary>Touchpad surface width in the DualSense / DS4 native range.</summary>
    private const int TouchpadMaxX = 1919;

    /// <summary>Touchpad surface height in the DualSense / DS4 native range.</summary>
    private const int TouchpadMaxY = 1079;

    private static ushort TouchX(float normalized) => ToSurface(normalized, TouchpadMaxX);

    private static ushort TouchY(float normalized) => ToSurface(normalized, TouchpadMaxY);

    private static ushort ToSurface(float normalized, int max)
    {
        if (!float.IsFinite(normalized))
        {
            return 0;
        }

        return (ushort)MathF.Round(Math.Clamp(normalized, 0f, 1f) * max);
    }
}
