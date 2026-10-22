using System.Runtime.Versioning;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using HIDMaestro;

namespace GameFlow.HidMaestro;

/// <summary>
/// Maps one <see cref="ControllerSnapshot"/> onto an <c>HMGamepadState</c>.
/// Any field left unwritten still goes out on the wire, as zero, so every
/// field the SDK reads is written on every frame.
/// </summary>
/// <remarks>
/// The writer holds the profile only. It never touches an <c>HMController</c>,
/// so it works without a device or admin rights.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class HidMaestroStateWriter
{
    // The Touchpad click maps to Touchpad, not Share: Sony profiles carry
    // Touchpad and mark Share as absent. Paddles pair by side, as SDL does.
    private static readonly (ButtonId Source, HMButton Target)[] Buttons =
    [
        (ButtonId.South, HMButton.A),
        (ButtonId.East, HMButton.B),
        (ButtonId.West, HMButton.X),
        (ButtonId.North, HMButton.Y),
        (ButtonId.LeftShoulder, HMButton.LeftBumper),
        (ButtonId.RightShoulder, HMButton.RightBumper),
        (ButtonId.Guide, HMButton.Guide),
        (ButtonId.Touchpad, HMButton.Touchpad),
        (ButtonId.Back, HMButton.Back),
        (ButtonId.Start, HMButton.Start),
        (ButtonId.LeftStick, HMButton.LeftStick),
        (ButtonId.RightStick, HMButton.RightStick),
        (ButtonId.Paddle1, HMButton.LeftPaddle),
        (ButtonId.Paddle2, HMButton.RightPaddle),
        (ButtonId.Paddle3, HMButton.LeftPaddle2),
        (ButtonId.Paddle4, HMButton.RightPaddle2),
        (ButtonId.Misc1, HMButton.Misc1),
    ];

    /// <summary>The button table, in submit order.</summary>
    internal static IReadOnlyList<(ButtonId Source, HMButton Target)> ButtonMap => Buttons;

    private readonly HMAxis? leftX;
    private readonly HMAxis? leftY;
    private readonly HMAxis? rightX;
    private readonly HMAxis? rightY;
    private readonly HMAxis? leftTrigger;
    private readonly HMAxis? rightTrigger;

    /// <summary>
    /// Resolves the six axis keys once. It asks the SDK's own helper, so the
    /// fallback rules (no sticks, axis maps) are never copied here. Each
    /// role gets a distinct value, so the key that carries it is known.
    /// </summary>
    public HidMaestroStateWriter(HMProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var probe = HMGamepadStateHelpers.StandardAxes(profile, 0.1f, 0.2f, 0.3f, 0.4f, 0.5f, 0.6f);
        var byRole = new Dictionary<float, HMAxis>(probe.Count);
        foreach (var pair in probe) { byRole[pair.Value] = pair.Key; }

        leftX = Find(byRole, 0.1f);
        leftY = Find(byRole, 0.2f);
        rightX = Find(byRole, 0.3f);
        rightY = Find(byRole, 0.4f);
        leftTrigger = Find(byRole, 0.5f);
        rightTrigger = Find(byRole, 0.6f);
    }

    /// <summary>
    /// Writes <paramref name="snapshot"/> into <paramref name="state"/>. The
    /// axis dictionary is created once and then updated in place, so the
    /// steady state allocates nothing.
    /// </summary>
    public void Fill(ref HMGamepadState state, ControllerSnapshot snapshot)
    {
        var axes = state.Axes ??= new Dictionary<HMAxis, float>(6);
        Write(axes, leftX, HidMaestroSubmission.ToAxis(snapshot.LeftStick.X));
        Write(axes, leftY, HidMaestroSubmission.ToAxis(snapshot.LeftStick.Y));
        Write(axes, rightX, HidMaestroSubmission.ToAxis(snapshot.RightStick.X));
        Write(axes, rightY, HidMaestroSubmission.ToAxis(snapshot.RightStick.Y));
        Write(axes, leftTrigger, HidMaestroSubmission.ToTrigger(snapshot.LeftTrigger));
        Write(axes, rightTrigger, HidMaestroSubmission.ToTrigger(snapshot.RightTrigger));

        var buttons = HMButton.None;
        foreach (var (source, target) in Buttons)
        {
            if (snapshot.IsPressed(source)) { buttons |= target; }
        }
        state.Buttons = buttons;

        state.Hat = HidMaestroSubmission.ResolveHat(snapshot) switch
        {
            HatDirection.North => HMHat.North,
            HatDirection.NorthEast => HMHat.NorthEast,
            HatDirection.East => HMHat.East,
            HatDirection.SouthEast => HMHat.SouthEast,
            HatDirection.South => HMHat.South,
            HatDirection.SouthWest => HMHat.SouthWest,
            HatDirection.West => HMHat.West,
            HatDirection.NorthWest => HMHat.NorthWest,
            _ => HMHat.None,
        };

        state.BatteryLevel = HidMaestroSubmission.BatteryLevelFor(snapshot);
        state.BatteryCharging = snapshot.BatteryCharging;
        state.BatteryFull = snapshot.BatteryPercent is null or >= 100;

        var motion = HidMaestroSubmission.ConvertMotion(snapshot);
        if (motion.Present)
        {
            state.AccelGX = motion.AccelG.X;
            state.AccelGY = motion.AccelG.Y;
            state.AccelGZ = motion.AccelG.Z;
            state.GyroDpsX = motion.GyroDps.X;
            state.GyroDpsY = motion.GyroDps.Y;
            state.GyroDpsZ = motion.GyroDps.Z;
        }
        else
        {
            // A pad with no sensor reports zeroes, not the last frame values.
            state.AccelGX = state.AccelGY = state.AccelGZ = 0f;
            state.GyroDpsX = state.GyroDpsY = state.GyroDpsZ = 0f;
        }

        var touch = HidMaestroSubmission.ConvertTouch(snapshot);
        state.TouchpadFinger0Active = touch.Finger0Down;
        state.TouchpadFinger0X = touch.Finger0X;
        state.TouchpadFinger0Y = touch.Finger0Y;
        state.TouchpadFinger0Id = touch.Finger0Id;
        state.TouchpadFinger1Active = touch.Finger1Down;
        state.TouchpadFinger1X = touch.Finger1X;
        state.TouchpadFinger1Y = touch.Finger1Y;
        state.TouchpadFinger1Id = touch.Finger1Id;
    }

    private static HMAxis? Find(Dictionary<float, HMAxis> byRole, float sentinel) =>
        byRole.TryGetValue(sentinel, out var key) ? key : null;

    private static void Write(Dictionary<HMAxis, float> axes, HMAxis? key, float value)
    {
        if (key is { } axis) { axes[axis] = value; }
    }
}
