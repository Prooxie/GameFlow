using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Input;

/// <summary>
/// Mouse input for one device since the last read: accumulated relative
/// movement (<see cref="Dx"/>/<see cref="Dy"/>, pixels) plus current
/// button levels. Movement is consumed (reset) on read so each pipeline
/// tick gets the delta since the previous tick.
/// </summary>
/// <param name="Dx">Accumulated horizontal movement since the last read (raw counts).</param>
/// <param name="Dy">Accumulated vertical movement since the last read (raw counts).</param>
/// <param name="Left">Left button is currently held.</param>
/// <param name="Right">Right button is currently held.</param>
/// <param name="Middle">Middle button (wheel click) is currently held.</param>
/// <param name="Button4">Side button 4 (back) is currently held.</param>
/// <param name="Button5">Side button 5 (forward) is currently held.</param>
/// <param name="WheelDelta">Accumulated vertical scroll since the last read,
/// in Windows wheel units (multiples of 120; positive = scroll up).</param>
public readonly record struct MouseFrame(
    int Dx, int Dy, bool Left, bool Right, bool Middle, bool Button4, bool Button5,
    int WheelDelta = 0);

/// <summary>Per-mouse-device source of movement + button state.</summary>
public interface IMouseStateSource
{
    /// <summary>Accumulated movement since the last call (resets) + current buttons.</summary>
    MouseFrame ReadMouseFrame(string deviceId);

    /// <summary>Movement for the device preview, independent of runtime reads.</summary>
    MouseFrame ReadMousePreviewFrame(string deviceId) => default;

    /// <summary>
    /// Combined frame across EVERY tracked mouse (summed deltas, OR'd
    /// buttons). Fallback for the composite-HID id mismatch — see
    /// <see cref="IKeyboardStateSource.GetPressedKeysAggregate"/>.
    /// </summary>
    MouseFrame ReadMouseFrameAggregate();
}

/// <summary>No-op default: every mouse reports no movement and no buttons.</summary>
public sealed class NullMouseStateSource : IMouseStateSource
{
    public MouseFrame ReadMouseFrameAggregate() => default;
    public MouseFrame ReadMouseFrame(string deviceId) => default;
}

/// <summary>
/// Turns raw mouse counts into a steady stick deflection.
///
/// <para>
/// The old conversion divided the movement since the last read by a fixed
/// "pixels for full deflection". That only means anything at one read
/// rate: at the 1000 Hz the runtime now polls, a read covers a millisecond,
/// holds one or two counts, and the stick barely moved however fast the
/// hand did. Worse, a 1000 Hz read of a 125 Hz mouse sees movement on one
/// read in eight, so the stick flickered between full and nothing.
/// </para>
///
/// <para>
/// This measures SPEED (counts per second over the real elapsed time) and
/// smooths it over a few tens of milliseconds, so the result depends on how
/// fast the mouse moves and not on how often anyone asks.
/// </para>
/// </summary>
public sealed class MouseMotionFilter
{
    /// <summary>Smoothing time constant. Long enough to bridge a 125 Hz mouse's gaps, short enough not to feel like lag.</summary>
    private const double TimeConstantSeconds = 0.025;

    private double velocityX;
    private double velocityY;

    /// <summary>
    /// Folds one read into the smoothed velocity and returns the stick
    /// deflection, −1‥1 with Y up.
    /// </summary>
    /// <param name="dx">Counts moved right since the previous read.</param>
    /// <param name="dy">Counts moved DOWN since the previous read (screen convention).</param>
    /// <param name="elapsedSeconds">Real time since the previous read.</param>
    /// <param name="map">Sensitivity, anti-deadzone and inversion.</param>
    public StickVector Update(int dx, int dy, double elapsedSeconds, KeyboardMouseMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        // A long gap (the app was busy, or nothing read this mouse for a
        // while) must not turn the next delta into a huge speed, nor keep
        // an old speed alive; a zero or negative one would divide by zero.
        var elapsed = Math.Clamp(elapsedSeconds, 0.0005, 0.1);
        var alpha = 1 - Math.Exp(-elapsed / TimeConstantSeconds);
        velocityX += ((dx / elapsed) - velocityX) * alpha;
        velocityY += ((dy / elapsed) - velocityY) * alpha;

        var fullScale = KeyboardMouseMap.MouseFullDeflectionCountsPerSecond / Math.Clamp(map.MouseSensitivity, 0.1, 10);
        var x = velocityX / fullScale;
        var y = -velocityY / fullScale; // screen Y down → stick up
        if (map.InvertMouseY)
        {
            y = -y;
        }

        var magnitude = Math.Sqrt((x * x) + (y * y));
        if (magnitude < 0.002)
        {
            // Below anything a game could act on: settle to exactly zero so
            // a resting mouse reads as a centred stick, not as drift.
            return StickVector.Zero;
        }

        // Anti-deadzone: lift the smallest deliberate movement out of the
        // game's own stick deadzone, then keep the rest proportional.
        var floor = Math.Clamp(map.MouseAntiDeadzone, 0, 0.5);
        var shaped = Math.Min(1, floor + ((1 - floor) * Math.Min(1, magnitude)));
        var scale = shaped / magnitude;
        return new StickVector((float)(x * scale), (float)(y * scale));
    }

    /// <summary>Forgets the speed, e.g. when the device was not read for a while.</summary>
    public void Reset()
    {
        velocityX = 0;
        velocityY = 0;
    }
}

/// <summary>
/// Turns a mouse into a <see cref="ControllerSnapshot"/> per the user's
/// <see cref="CompiledKeyboardMouseMap"/>: buttons and wheel notches
/// produce whatever controls they are bound to, and movement (already
/// converted to a deflection by <see cref="MouseMotionFilter"/>) drives
/// the chosen stick.
/// </summary>
public static class MouseGamepadSynthesizer
{
    /// <summary>How long a single wheel notch stays pressed — long enough for a game polling at 30 Hz to see it.</summary>
    public static readonly TimeSpan WheelHoldTime = TimeSpan.FromMilliseconds(60);

    /// <param name="deviceName">Name stamped on the snapshot.</param>
    /// <param name="frame">Buttons currently held (movement is read through <paramref name="motion"/>).</param>
    /// <param name="motion">Deflection from <see cref="MouseMotionFilter"/>, Y up.</param>
    /// <param name="wheel">Wheel directions to report as held this tick (latched by the caller).</param>
    /// <param name="map">The binding map.</param>
    public static ControllerSnapshot Synthesize(
        string deviceName,
        MouseFrame frame,
        StickVector motion,
        MouseButtonFlags wheel,
        CompiledKeyboardMouseMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var held = wheel & (MouseButtonFlags.WheelUp | MouseButtonFlags.WheelDown);
        if (frame.Left) { held |= MouseButtonFlags.Left; }
        if (frame.Right) { held |= MouseButtonFlags.Right; }
        if (frame.Middle) { held |= MouseButtonFlags.Middle; }
        if (frame.Button4) { held |= MouseButtonFlags.Back; }
        if (frame.Button5) { held |= MouseButtonFlags.Forward; }

        var accumulator = new GamepadActionAccumulator();
        ApplyIfHeld(ref accumulator, held, MouseButtonFlags.Left, MouseInput.Left, map);
        ApplyIfHeld(ref accumulator, held, MouseButtonFlags.Right, MouseInput.Right, map);
        ApplyIfHeld(ref accumulator, held, MouseButtonFlags.Middle, MouseInput.Middle, map);
        ApplyIfHeld(ref accumulator, held, MouseButtonFlags.Back, MouseInput.Back, map);
        ApplyIfHeld(ref accumulator, held, MouseButtonFlags.Forward, MouseInput.Forward, map);
        ApplyIfHeld(ref accumulator, held, MouseButtonFlags.WheelUp, MouseInput.WheelUp, map);
        ApplyIfHeld(ref accumulator, held, MouseButtonFlags.WheelDown, MouseInput.WheelDown, map);

        var snapshot = accumulator.ToSnapshot(deviceName);
        snapshot = map.Source.MouseMotion switch
        {
            MouseMotionTarget.LeftStick => snapshot with { LeftStick = Strongest(snapshot.LeftStick, motion) },
            MouseMotionTarget.RightStick => snapshot with { RightStick = Strongest(snapshot.RightStick, motion) },
            _ => snapshot,
        };

        return snapshot with { MouseButtons = held, MouseMotion = motion };
    }

    private static void ApplyIfHeld(
        ref GamepadActionAccumulator accumulator,
        MouseButtonFlags held,
        MouseButtonFlags flag,
        MouseInput input,
        CompiledKeyboardMouseMap map)
    {
        if ((held & flag) == 0)
        {
            return;
        }

        foreach (var action in map.ActionsFor(input))
        {
            accumulator.Apply(action);
        }
    }

    /// <summary>A mouse button bound to a stick direction and movement on the same stick: the larger push wins, as between two devices.</summary>
    private static StickVector Strongest(StickVector a, StickVector b) =>
        (a.X * a.X) + (a.Y * a.Y) >= (b.X * b.X) + (b.Y * b.Y) ? a : b;
}
