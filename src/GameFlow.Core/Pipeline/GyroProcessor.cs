using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;

namespace GameFlow.Core.Pipeline;

/// <summary>Result of one gyro rule for one tick, in normalized aim units before target conversion.</summary>
public readonly record struct GyroAimResult(bool Engaged, float Horizontal, float Vertical);

/// <summary>
/// Turns raw angular velocity into aim. Owned per
/// <see cref="ControllerMappingPipeline"/> instance, like every other
/// stateful helper here — the smoothing window and toggle state belong
/// to one slot's live session.
///
/// <para><b>Sign derivation.</b> SDL documents gyro as radians/second,
/// positive counter-clockwise (right-hand rule), with axes X=right,
/// Y=up, Z=toward you, and values [0]=pitch, [1]=yaw, [2]=roll.
/// Working the right-hand rule through from that:</para>
/// <list type="bullet">
///   <item>Positive <b>yaw</b> (around +Y, the up axis) turns the pad's
///   nose LEFT. Aiming left is negative X, so horizontal = -yaw.</item>
///   <item>Positive <b>pitch</b> (around +X, the right axis) rotates the
///   top of the pad toward you, i.e. the nose rises. Aiming up is
///   positive Y in this codebase's stick convention, so
///   vertical = +pitch.</item>
/// </list>
/// <para>Both are still exposed as invert toggles, because which
/// direction feels "correct" is genuinely a matter of preference, not
/// just a convention to get right once.</para>
/// </summary>
public sealed class GyroProcessor
{
    /// <summary>
    /// Angular velocity (rad/s) that produces full stick deflection at
    /// sensitivity 1.0. ~4 rad/s is a brisk but controlled turn, so 1.0
    /// lands in a usable place and the multiplier scales from there.
    /// Not from spec — a documented, tunable reference point.
    /// </summary>
    private const float FullDeflectionRadiansPerSecond = 4.0f;

    /// <summary>
    /// Screen pixels per radian of controller rotation at sensitivity
    /// 1.0 for mouse output. ~1000 means a 90° turn (≈1.57 rad) sweeps
    /// roughly a 1600px screen width. Same status as the constant above:
    /// a documented starting point, not a spec value.
    /// </summary>
    private const float MousePixelsPerRadian = 1000f;

    private sealed class GyroState
    {
        public readonly Queue<(float X, float Y)> Window = new();
        public float SumX;
        public float SumY;
        public bool ToggleEngaged;
        public bool ButtonWasPressed;
    }

    private readonly Dictionary<string, GyroState> states = new(StringComparer.Ordinal);

    /// <summary>
    /// Computes this tick's aim contribution for one rule. Returns
    /// <see cref="GyroAimResult.Engaged"/> = false (and zero movement)
    /// when the pad has no gyro or the rule isn't currently engaged.
    /// </summary>
    public GyroAimResult Process(GyroMapRule rule, ControllerSnapshot physical)
    {
        var state = states.TryGetValue(rule.Id, out var existing)
            ? existing
            : states[rule.Id] = new GyroState();

        var engaged = ResolveEngaged(rule, physical, state);
        if (!engaged || !physical.HasGyro)
        {
            // Clear the smoothing window so re-engaging doesn't blend in
            // stale motion from before the gyro was silenced.
            state.Window.Clear();
            state.SumX = 0f;
            state.SumY = 0f;
            return new GyroAimResult(false, 0f, 0f);
        }

        // Bias (drift) correction first — everything downstream assumes
        // "zero means genuinely still".
        var pitch = physical.GyroPitch - rule.BiasPitch;
        var yaw = physical.GyroYaw - rule.BiasYaw;
        var roll = physical.GyroRoll - rule.BiasRoll;

        var (horizontal, vertical) = ProjectToAim(rule.ReferenceFrame, pitch, yaw, roll, physical);

        // Residual-drift deadzone, applied to the combined magnitude so
        // a slow diagonal creep is caught as readily as a single-axis one.
        if (MathF.Sqrt((horizontal * horizontal) + (vertical * vertical)) < rule.DeadzoneRadiansPerSecond)
        {
            horizontal = 0f;
            vertical = 0f;
        }

        (horizontal, vertical) = Smooth(rule, state, horizontal, vertical);

        if (rule.InvertX) { horizontal = -horizontal; }
        if (rule.InvertY) { vertical = -vertical; }

        return new GyroAimResult(true, horizontal * rule.SensitivityX, vertical * rule.SensitivityY);
    }

    /// <summary>
    /// Projects raw angular velocity onto aim axes per reference frame.
    /// Player and World both use the accelerometer as a "which way is
    /// down" reference; with no accelerometer reading they degrade to
    /// <see cref="GyroReferenceFrame.Local"/> rather than producing
    /// garbage from a zero gravity vector.
    /// </summary>
    private static (float Horizontal, float Vertical) ProjectToAim(
        GyroReferenceFrame frame, float pitch, float yaw, float roll, ControllerSnapshot physical)
    {
        // Vertical is local pitch in every frame: tilting the pad up
        // means "aim up" regardless of how it's rotated about vertical.
        var vertical = pitch;

        if (frame == GyroReferenceFrame.Local)
        {
            return (-yaw, vertical);
        }

        // At rest the accelerometer measures the reaction to gravity,
        // which points UP in device coordinates — so the normalized
        // vector IS world-up expressed in the pad's own axes.
        var magnitude = MathF.Sqrt(
            (physical.AccelX * physical.AccelX)
            + (physical.AccelY * physical.AccelY)
            + (physical.AccelZ * physical.AccelZ));

        if (magnitude < 0.0001f)
        {
            return (-yaw, vertical); // no accelerometer — fall back to Local
        }

        var gx = physical.AccelX / magnitude;
        var gy = physical.AccelY / magnitude;
        var gz = physical.AccelZ / magnitude;

        // Rotation about world-up = the component of the angular
        // velocity vector along world-up, i.e. their dot product.
        // World uses all three axes; Player deliberately drops pitch's
        // contribution, so looking up/down never bleeds into horizontal
        // aim (the practical difference between the two frames).
        var horizontal = frame == GyroReferenceFrame.World
            ? (pitch * gx) + (yaw * gy) + (roll * gz)
            : (yaw * gy) + (roll * gz);

        return (-horizontal, vertical);
    }

    /// <summary>
    /// Dual-threshold smoothing: fully smoothed below the lower
    /// threshold (kills tremor while holding still), fully raw above the
    /// upper one (keeps flicks sharp), linear blend between.
    /// </summary>
    private static (float X, float Y) Smooth(GyroMapRule rule, GyroState state, float x, float y)
    {
        var windowSize = Math.Max(1, rule.SmoothingWindowSamples);
        if (rule.SmoothingLowerThreshold <= 0f || windowSize == 1)
        {
            return (x, y);
        }

        state.Window.Enqueue((x, y));
        state.SumX += x;
        state.SumY += y;
        while (state.Window.Count > windowSize)
        {
            var (oldX, oldY) = state.Window.Dequeue();
            state.SumX -= oldX;
            state.SumY -= oldY;
        }

        var averageX = state.SumX / state.Window.Count;
        var averageY = state.SumY / state.Window.Count;

        var magnitude = MathF.Sqrt((x * x) + (y * y));
        var upper = MathF.Max(rule.SmoothingUpperThreshold, rule.SmoothingLowerThreshold + 0.0001f);
        var directWeight = Math.Clamp(
            (magnitude - rule.SmoothingLowerThreshold) / (upper - rule.SmoothingLowerThreshold), 0f, 1f);

        return (
            (x * directWeight) + (averageX * (1f - directWeight)),
            (y * directWeight) + (averageY * (1f - directWeight)));
    }

    private static bool ResolveEngaged(GyroMapRule rule, ControllerSnapshot physical, GyroState state)
    {
        var buttonPressed = rule.EngageButton != ButtonId.None && physical.IsPressed(rule.EngageButton);
        var wasPressed = state.ButtonWasPressed;
        state.ButtonWasPressed = buttonPressed;

        // Stick gate reads the RAW physical sticks deliberately — the
        // point is that a nudge too small for the game to act on still
        // arms aiming, so it must be read before any deadzone shaping.
        var stickGate = rule.StickGateEnabled
            && (physical.LeftStick.Magnitude > rule.StickGateThreshold
                || physical.RightStick.Magnitude > rule.StickGateThreshold);

        switch (rule.EngageMode)
        {
            case GyroEngageMode.AlwaysOn:
                return true;

            case GyroEngageMode.HoldToDisable:
                // An explicit "silence the gyro" hold outranks the stick
                // gate — otherwise moving a stick would defeat the very
                // thing the button is being held for.
                return !buttonPressed;

            case GyroEngageMode.Toggle:
                if (buttonPressed && !wasPressed)
                {
                    state.ToggleEngaged = !state.ToggleEngaged;
                }
                return state.ToggleEngaged || stickGate;

            case GyroEngageMode.HoldToEngage:
            default:
                return buttonPressed || stickGate;
        }
    }

    /// <summary>Converts an aim result to stick deflection (-1..1 per axis).</summary>
    public static StickVector ToStick(GyroAimResult aim)
    {
        return new StickVector(
            Math.Clamp(aim.Horizontal / FullDeflectionRadiansPerSecond, -1f, 1f),
            Math.Clamp(aim.Vertical / FullDeflectionRadiansPerSecond, -1f, 1f));
    }

    /// <summary>
    /// Converts an aim result to this tick's mouse movement in pixels.
    /// Angular velocity is a RATE, so the elapsed time matters —
    /// unlike the touchpad's mouse mode, which already works in
    /// per-frame position deltas.
    /// </summary>
    public static (float Dx, float Dy) ToMouseDelta(GyroAimResult aim, float deltaSeconds)
    {
        var scale = MousePixelsPerRadian * deltaSeconds;
        // Screen Y grows downward while aim Y grows upward, so vertical
        // is negated here — the same axis flip the touchpad stick mode
        // needed, in the opposite direction.
        return (aim.Horizontal * scale, -aim.Vertical * scale);
    }
}
