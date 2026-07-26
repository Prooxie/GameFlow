using System.Text.Json.Serialization;
using GameFlow.Core.Enums;

namespace GameFlow.Core.Models.Rules;

/// <summary>
/// "Aim with the controller, not the stick." Turns the pad's angular
/// velocity into stick deflection or mouse movement.
///
/// <para>
/// The raw input is <see cref="ControllerSnapshot.GyroPitch"/>/
/// <see cref="ControllerSnapshot.GyroYaw"/>/<see cref="ControllerSnapshot.GyroRoll"/>
/// in radians per second (SDL's documented unit, verified against
/// upstream headers). <see cref="Pipeline.GyroProcessor"/> converts
/// that to output; this record is purely the tuning.
/// </para>
/// </summary>
public sealed record GyroMapRule : MappingRule
{
    [JsonPropertyName("referenceFrame")]
    public GyroReferenceFrame ReferenceFrame { get; init; } = GyroReferenceFrame.Player;

    [JsonPropertyName("outputTarget")]
    public GyroOutputTarget OutputTarget { get; init; } = GyroOutputTarget.Stick;

    /// <summary>Used when <see cref="OutputTarget"/> is <see cref="GyroOutputTarget.Stick"/>.</summary>
    [JsonPropertyName("targetStick")]
    public StickId TargetStick { get; init; } = StickId.Right;

    /// <summary>Horizontal sensitivity multiplier. 1.0 is the processor's documented reference scale.</summary>
    [JsonPropertyName("sensitivityX")]
    public float SensitivityX { get; init; } = 1.0f;

    [JsonPropertyName("sensitivityY")]
    public float SensitivityY { get; init; } = 1.0f;

    [JsonPropertyName("invertX")]
    public bool InvertX { get; init; }

    [JsonPropertyName("invertY")]
    public bool InvertY { get; init; }

    // ─── Engage gating ───

    [JsonPropertyName("engageMode")]
    public GyroEngageMode EngageMode { get; init; } = GyroEngageMode.AlwaysOn;

    [JsonPropertyName("engageButton")]
    public ButtonId EngageButton { get; init; } = ButtonId.None;

    /// <summary>
    /// When set, moving EITHER stick past
    /// <see cref="StickGateThreshold"/> also engages the gyro. Read from
    /// the raw physical stick BEFORE this profile's deadzone processing,
    /// so a nudge too small for the game to act on still arms aiming.
    /// </summary>
    [JsonPropertyName("stickGateEnabled")]
    public bool StickGateEnabled { get; init; }

    [JsonPropertyName("stickGateThreshold")]
    public float StickGateThreshold { get; init; } = 0.1f;

    // ─── Smoothing ───

    /// <summary>
    /// Dual-threshold smoothing. Movement slower than this is fully
    /// smoothed, which removes hand tremor and sensor noise while
    /// you're holding still. 0 disables smoothing entirely.
    /// </summary>
    [JsonPropertyName("smoothingLowerThreshold")]
    public float SmoothingLowerThreshold { get; init; } = 0.08f;

    /// <summary>
    /// Movement faster than this is passed through completely
    /// unsmoothed, so quick flicks stay sharp. Between the two
    /// thresholds the raw and smoothed signals blend linearly.
    /// </summary>
    [JsonPropertyName("smoothingUpperThreshold")]
    public float SmoothingUpperThreshold { get; init; } = 0.25f;

    /// <summary>How many recent samples the smoothed signal averages over.</summary>
    [JsonPropertyName("smoothingWindowSamples")]
    public int SmoothingWindowSamples { get; init; } = 8;

    // ─── Calibration ───

    /// <summary>
    /// Per-axis drift correction in radians/second, subtracted from
    /// every raw reading. Gyros report a small non-zero rate even at
    /// rest, which without this makes the aim creep on its own. Captured
    /// by holding the pad still and averaging — that capture UI is not
    /// built yet, so for now these are set by hand in the profile JSON.
    /// </summary>
    [JsonPropertyName("biasPitch")]
    public float BiasPitch { get; init; }

    [JsonPropertyName("biasYaw")]
    public float BiasYaw { get; init; }

    [JsonPropertyName("biasRoll")]
    public float BiasRoll { get; init; }

    /// <summary>Angular velocity below this magnitude (after bias correction) is treated as zero — a final guard against residual drift.</summary>
    [JsonPropertyName("deadzoneRadiansPerSecond")]
    public float DeadzoneRadiansPerSecond { get; init; } = 0.02f;
}
