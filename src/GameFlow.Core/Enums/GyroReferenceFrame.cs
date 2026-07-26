namespace GameFlow.Core.Enums;

/// <summary>
/// How raw gyro axes are interpreted into aim. See
/// <see cref="Pipeline.GyroProcessor"/> for the actual math and the
/// derivation of each mode from SDL's documented axis convention.
/// </summary>
public enum GyroReferenceFrame
{
    /// <summary>
    /// Raw controller axes, no correction: yaw turns you horizontally,
    /// pitch aims vertically. Exact and predictable, but if you hold the
    /// pad tilted, "horizontal" tilts with it.
    /// </summary>
    Local,

    /// <summary>
    /// Combines yaw and roll using gravity, so twisting the pad about
    /// the world's vertical always reads as horizontal aim even when
    /// you hold it leaned. Ignores pitch's contribution to horizontal.
    /// </summary>
    Player,

    /// <summary>
    /// Full projection of the angular velocity onto the world's vertical
    /// axis (from gravity), so horizontal aim is correct at any
    /// orientation, including holding the pad sideways.
    /// </summary>
    World
}

/// <summary>When a <see cref="Models.Rules.GyroMapRule"/> is actually producing output.</summary>
public enum GyroEngageMode
{
    /// <summary>Gyro always drives its target.</summary>
    AlwaysOn,

    /// <summary>Gyro only drives its target while the engage button is held ("Aim Engage").</summary>
    HoldToEngage,

    /// <summary>Inverse: gyro is live by default, and holding the button silences it.</summary>
    HoldToDisable,

    /// <summary>Press to turn gyro on, press again to turn it off.</summary>
    Toggle
}

/// <summary>What a <see cref="Models.Rules.GyroMapRule"/> drives.</summary>
public enum GyroOutputTarget
{
    /// <summary>A virtual stick — works with any game that reads a gamepad.</summary>
    Stick,

    /// <summary>The mouse cursor, via the same per-tick delta channel the touchpad mouse mode uses.</summary>
    Mouse
}
