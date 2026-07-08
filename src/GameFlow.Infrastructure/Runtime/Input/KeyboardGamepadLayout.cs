using System.Collections.Frozen;

namespace GameFlow.Infrastructure.Runtime.Input;

/// <summary>What a keyboard key does when treated as a gamepad input.</summary>
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
}

/// <summary>One row of the keyboard-as-gamepad reference shown in the UI.</summary>
public sealed record KeyboardGamepadBinding(string KeyLabel, string MapsTo);

/// <summary>
/// The fixed default keyboard-as-gamepad layout. Maps Windows virtual-key
/// codes to <see cref="GamepadAction"/>s, with a UI-friendly description.
/// Customization is a follow-up; for now the layout is constant so the
/// synthesizer + UI can ship and the raw-input reader can plug in next.
/// </summary>
public static class KeyboardGamepadLayout
{
    public static IReadOnlyList<KeyboardGamepadBinding> DefaultDescription { get; } =
    [
        new("W / A / S / D",   "Left stick (up / left / down / right)"),
        new("I / J / K / L",   "Right stick (up / left / down / right)"),
        new("Arrow keys",      "D-pad (up / down / left / right)"),
        new("Space",           "A  (South)"),
        new("C",               "B  (East)"),
        new("X",               "X  (West)"),
        new("V",               "Y  (North)"),
        new("Q  /  E",         "Left shoulder  /  Right shoulder"),
        new("1  /  3",         "Left trigger  /  Right trigger  (digital, full press)"),
        new("Tab",             "Back / Select"),
        new("Enter",           "Start"),
        new("Backspace",       "Guide / Home"),
    ];

    /// <summary>
    /// Windows virtual-key code → gamepad action. Frozen for read-optimized
    /// lookups — this is consulted per pressed key on every synthesizer tick.
    /// </summary>
    public static FrozenDictionary<int, GamepadAction> Default { get; } =
        new Dictionary<int, GamepadAction>
        {
            // Left stick — WASD
            [0x57] = GamepadAction.LeftStickUp,
            [0x41] = GamepadAction.LeftStickLeft,
            [0x53] = GamepadAction.LeftStickDown,
            [0x44] = GamepadAction.LeftStickRight,
            // Right stick — IJKL
            [0x49] = GamepadAction.RightStickUp,
            [0x4A] = GamepadAction.RightStickLeft,
            [0x4B] = GamepadAction.RightStickDown,
            [0x4C] = GamepadAction.RightStickRight,
            // D-pad — arrow keys
            [0x26] = GamepadAction.DpadUp,
            [0x28] = GamepadAction.DpadDown,
            [0x25] = GamepadAction.DpadLeft,
            [0x27] = GamepadAction.DpadRight,
            // Face buttons
            [0x20] = GamepadAction.South,   // Space
            [0x43] = GamepadAction.East,    // C
            [0x58] = GamepadAction.West,    // X
            [0x56] = GamepadAction.North,   // V
            // Shoulders
            [0x51] = GamepadAction.LeftShoulder,    // Q
            [0x45] = GamepadAction.RightShoulder,   // E
            // Triggers (digital — full press)
            [0x31] = GamepadAction.LeftTriggerFull,  // 1
            [0x33] = GamepadAction.RightTriggerFull, // 3
            // System
            [0x09] = GamepadAction.Back,    // Tab
            [0x0D] = GamepadAction.Start,   // Enter
            [0x08] = GamepadAction.Guide,   // Backspace
        }.ToFrozenDictionary();
}
