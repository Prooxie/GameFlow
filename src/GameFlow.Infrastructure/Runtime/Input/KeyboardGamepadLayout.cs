namespace GameFlow.Infrastructure.Runtime.Input;

/// <summary>
/// A controller control that a keyboard key or mouse input can produce.
/// See <see cref="KeyboardMouseMap"/> for how inputs are bound to these.
/// </summary>
/// <remarks>
/// Persisted by name in the keyboard &amp; mouse map, so values may be
/// appended but never renamed.
/// </remarks>
public enum GamepadAction
{
    None,
    South, East, West, North,
    LeftShoulder, RightShoulder,
    LeftTriggerFull, RightTriggerFull,
    Back, Start, Guide,
    DpadUp, DpadDown, DpadLeft, DpadRight,
    LeftStickUp, LeftStickDown, LeftStickLeft, LeftStickRight,
    RightStickUp, RightStickDown, RightStickLeft, RightStickRight,
    LeftStickClick, RightStickClick,

    /// <summary>Held: the left stick moves at half deflection, the usual keyboard "walk" key.</summary>
    LeftStickWalk,

    Touchpad,
    Misc,
}
