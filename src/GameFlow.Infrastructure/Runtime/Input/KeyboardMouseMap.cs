using System.Collections.Frozen;

namespace GameFlow.Infrastructure.Runtime.Input;

/// <summary>A mouse input that can stand in for a controller control.</summary>
public enum MouseInput
{
    None,
    Left,
    Right,
    Middle,

    /// <summary>Side button 4 (browser back).</summary>
    Back,

    /// <summary>Side button 5 (browser forward).</summary>
    Forward,

    WheelUp,
    WheelDown,
}

/// <summary>Which stick mouse movement drives.</summary>
public enum MouseMotionTarget
{
    None,
    LeftStick,
    RightStick,
}

/// <summary>
/// One keyboard key or mouse input bound to one controller control.
/// Exactly one of <see cref="Key"/> and <see cref="Mouse"/> is set.
/// </summary>
/// <param name="Action">The controller control this input produces.</param>
/// <param name="Key">Windows virtual-key code, or 0 when bound to a mouse input.</param>
/// <param name="Mouse">Mouse input, or <see cref="MouseInput.None"/> when bound to a key.</param>
public sealed record KeyboardMouseBinding(GamepadAction Action, int Key = 0, MouseInput Mouse = MouseInput.None)
{
    public bool IsKey => KeyboardVirtualKeys.IsSupported(Key) && Mouse == MouseInput.None;

    public bool IsMouse => Key == 0 && Mouse != MouseInput.None;

    public bool IsValid => Action != GamepadAction.None && (IsKey || IsMouse);

    /// <summary>What the input is called on screen: a key name or a mouse button.</summary>
    public string InputLabel => IsKey ? VirtualKeyNames.GetName(Key) : KeyboardMouseMap.MouseLabel(Mouse);
}

/// <summary>
/// How a keyboard and a mouse become a controller: every controller
/// control can be bound to any number of keys and mouse inputs, and mouse
/// movement drives one stick.
///
/// <para>
/// The store keeps shared defaults and optional overrides by physical-device
/// identity. Multiple HID collections of a keyboard share their canonical
/// device identity; independent keyboards and mice keep separate maps.
/// </para>
/// </summary>
public sealed record KeyboardMouseMap
{
    /// <summary>Upper bound on stored bindings, so a damaged file cannot grow the per-tick lookup without limit.</summary>
    public const int MaxBindings = 256;

    public IReadOnlyList<KeyboardMouseBinding> Bindings { get; init; } = DefaultBindings;

    public MouseMotionTarget MouseMotion { get; init; } = MouseMotionTarget.RightStick;

    /// <summary>
    /// Multiplier on mouse speed, 0.1–10. At 1.0 a hand moving the mouse
    /// at <see cref="MouseFullDeflectionCountsPerSecond"/> counts a second
    /// pushes the stick all the way.
    /// </summary>
    public double MouseSensitivity { get; init; } = 1.0;

    /// <summary>
    /// Smallest deflection a moving mouse produces, 0–0.5. Games ignore a
    /// stick inside their own deadzone, which without this swallows every
    /// slow, precise aim adjustment.
    /// </summary>
    public double MouseAntiDeadzone { get; init; } = 0.1;

    public bool InvertMouseY { get; init; }

    /// <summary>Mouse speed (counts per second) that gives full deflection at sensitivity 1.</summary>
    public const double MouseFullDeflectionCountsPerSecond = 6000;

    /// <summary>The layout GameFlow shipped with before bindings were editable, plus the mouse.</summary>
    /// <remarks>
    /// Declared before <see cref="Default"/> on purpose: static initializers
    /// run in file order, and <see cref="Default"/> reads this list. With
    /// the order reversed the default map was built while this was still
    /// null, and every keyboard started with no bindings at all.
    /// </remarks>
    public static IReadOnlyList<KeyboardMouseBinding> DefaultBindings { get; } =
    [
        new(GamepadAction.LeftStickUp, Key: 0x57),     // W
        new(GamepadAction.LeftStickLeft, Key: 0x41),   // A
        new(GamepadAction.LeftStickDown, Key: 0x53),   // S
        new(GamepadAction.LeftStickRight, Key: 0x44),  // D
        new(GamepadAction.RightStickUp, Key: 0x49),    // I
        new(GamepadAction.RightStickLeft, Key: 0x4A),  // J
        new(GamepadAction.RightStickDown, Key: 0x4B),  // K
        new(GamepadAction.RightStickRight, Key: 0x4C), // L
        new(GamepadAction.DpadUp, Key: 0x26),
        new(GamepadAction.DpadDown, Key: 0x28),
        new(GamepadAction.DpadLeft, Key: 0x25),
        new(GamepadAction.DpadRight, Key: 0x27),
        new(GamepadAction.South, Key: 0x20),           // Space
        new(GamepadAction.East, Key: 0x43),            // C
        new(GamepadAction.West, Key: 0x58),            // X
        new(GamepadAction.North, Key: 0x56),           // V
        new(GamepadAction.LeftShoulder, Key: 0x51),    // Q
        new(GamepadAction.RightShoulder, Key: 0x45),   // E
        new(GamepadAction.LeftTriggerFull, Key: 0x31), // 1
        new(GamepadAction.RightTriggerFull, Key: 0x33),// 3
        new(GamepadAction.Back, Key: 0x09),            // Tab
        new(GamepadAction.Start, Key: 0x0D),           // Enter
        new(GamepadAction.Guide, Key: 0x08),           // Backspace
        new(GamepadAction.LeftStickClick, Key: 0xA0),  // Left Shift
        new(GamepadAction.RightTriggerFull, Mouse: MouseInput.Left),
        new(GamepadAction.LeftTriggerFull, Mouse: MouseInput.Right),
        new(GamepadAction.RightStickClick, Mouse: MouseInput.Middle),
        new(GamepadAction.LeftShoulder, Mouse: MouseInput.Back),
        new(GamepadAction.RightShoulder, Mouse: MouseInput.Forward),
    ];

    public static KeyboardMouseMap Default { get; } = new();

    /// <summary>Every bindable control, in the order the editor lists them.</summary>
    public static IReadOnlyList<GamepadAction> EditableActions { get; } =
    [
        GamepadAction.LeftStickUp, GamepadAction.LeftStickDown,
        GamepadAction.LeftStickLeft, GamepadAction.LeftStickRight,
        GamepadAction.LeftStickClick, GamepadAction.LeftStickWalk,
        GamepadAction.RightStickUp, GamepadAction.RightStickDown,
        GamepadAction.RightStickLeft, GamepadAction.RightStickRight,
        GamepadAction.RightStickClick,
        GamepadAction.DpadUp, GamepadAction.DpadDown, GamepadAction.DpadLeft, GamepadAction.DpadRight,
        GamepadAction.South, GamepadAction.East, GamepadAction.West, GamepadAction.North,
        GamepadAction.LeftShoulder, GamepadAction.RightShoulder,
        GamepadAction.LeftTriggerFull, GamepadAction.RightTriggerFull,
        GamepadAction.Back, GamepadAction.Start, GamepadAction.Guide,
        GamepadAction.Touchpad, GamepadAction.Misc,
    ];

    /// <summary>Plain name of a controller control, as the editor and the key hints show it.</summary>
    public static string ActionLabel(GamepadAction action) => action switch
    {
        GamepadAction.South => "A / Cross",
        GamepadAction.East => "B / Circle",
        GamepadAction.West => "X / Square",
        GamepadAction.North => "Y / Triangle",
        GamepadAction.LeftShoulder => "LB / L1",
        GamepadAction.RightShoulder => "RB / R1",
        GamepadAction.LeftTriggerFull => "LT / L2",
        GamepadAction.RightTriggerFull => "RT / R2",
        GamepadAction.Back => "Back / Share",
        GamepadAction.Start => "Start / Options",
        GamepadAction.Guide => "Guide / PS",
        GamepadAction.DpadUp => "D-pad up",
        GamepadAction.DpadDown => "D-pad down",
        GamepadAction.DpadLeft => "D-pad left",
        GamepadAction.DpadRight => "D-pad right",
        GamepadAction.LeftStickUp => "Left stick up",
        GamepadAction.LeftStickDown => "Left stick down",
        GamepadAction.LeftStickLeft => "Left stick left",
        GamepadAction.LeftStickRight => "Left stick right",
        GamepadAction.LeftStickClick => "Left stick click (L3)",
        GamepadAction.LeftStickWalk => "Walk (half left stick)",
        GamepadAction.RightStickUp => "Right stick up",
        GamepadAction.RightStickDown => "Right stick down",
        GamepadAction.RightStickLeft => "Right stick left",
        GamepadAction.RightStickRight => "Right stick right",
        GamepadAction.RightStickClick => "Right stick click (R3)",
        GamepadAction.Touchpad => "Touchpad click",
        GamepadAction.Misc => "Mute / Capture",
        _ => action.ToString(),
    };

    /// <summary>Short legend printed on a keycap or mouse button bound to a control.</summary>
    public static string ActionShortLabel(GamepadAction action) => action switch
    {
        GamepadAction.South => "A",
        GamepadAction.East => "B",
        GamepadAction.West => "X",
        GamepadAction.North => "Y",
        GamepadAction.LeftShoulder => "LB",
        GamepadAction.RightShoulder => "RB",
        GamepadAction.LeftTriggerFull => "LT",
        GamepadAction.RightTriggerFull => "RT",
        GamepadAction.Back => "Back",
        GamepadAction.Start => "Start",
        GamepadAction.Guide => "Home",
        GamepadAction.DpadUp => "D↑",
        GamepadAction.DpadDown => "D↓",
        GamepadAction.DpadLeft => "D←",
        GamepadAction.DpadRight => "D→",
        GamepadAction.LeftStickUp => "LS↑",
        GamepadAction.LeftStickDown => "LS↓",
        GamepadAction.LeftStickLeft => "LS←",
        GamepadAction.LeftStickRight => "LS→",
        GamepadAction.LeftStickClick => "L3",
        GamepadAction.LeftStickWalk => "Walk",
        GamepadAction.RightStickUp => "RS↑",
        GamepadAction.RightStickDown => "RS↓",
        GamepadAction.RightStickLeft => "RS←",
        GamepadAction.RightStickRight => "RS→",
        GamepadAction.RightStickClick => "R3",
        GamepadAction.Touchpad => "Pad",
        GamepadAction.Misc => "Mute",
        _ => string.Empty,
    };

    public static string MouseLabel(MouseInput input) => input switch
    {
        MouseInput.Left => "Left click",
        MouseInput.Right => "Right click",
        MouseInput.Middle => "Middle click",
        MouseInput.Back => "Mouse 4 (back)",
        MouseInput.Forward => "Mouse 5 (forward)",
        MouseInput.WheelUp => "Wheel up",
        MouseInput.WheelDown => "Wheel down",
        _ => string.Empty,
    };

    /// <summary>
    /// Returns a copy with out-of-range values clamped, invalid and
    /// duplicate bindings dropped and the list capped — the form the
    /// runtime is allowed to read, whatever was on disk.
    /// </summary>
    public KeyboardMouseMap Normalize()
    {
        var bindings = (Bindings ?? [])
            .Where(binding => binding is not null && binding.IsValid)
            .Distinct()
            .Take(MaxBindings)
            .ToArray();

        return this with
        {
            Bindings = bindings,
            MouseMotion = Enum.IsDefined(MouseMotion) ? MouseMotion : MouseMotionTarget.RightStick,
            MouseSensitivity = double.IsFinite(MouseSensitivity) ? Math.Clamp(MouseSensitivity, 0.1, 10) : 1.0,
            MouseAntiDeadzone = double.IsFinite(MouseAntiDeadzone) ? Math.Clamp(MouseAntiDeadzone, 0, 0.5) : 0.1,
        };
    }

    /// <summary>
    /// The per-tick lookup the synthesizers read, built once per map
    /// rather than scanning <see cref="Bindings"/> for every pressed key.
    /// </summary>
    public CompiledKeyboardMouseMap Compile() => new(Normalize());
}

/// <summary>Read-optimized form of a <see cref="KeyboardMouseMap"/>.</summary>
public sealed class CompiledKeyboardMouseMap
{
    private static readonly GamepadAction[] NoActions = [];

    internal CompiledKeyboardMouseMap(KeyboardMouseMap map)
    {
        Source = map;
        KeyActions = map.Bindings
            .Where(binding => binding.IsKey)
            .GroupBy(binding => binding.Key)
            .ToFrozenDictionary(group => group.Key, group => group.Select(b => b.Action).Distinct().ToArray());
        MouseActions = map.Bindings
            .Where(binding => binding.IsMouse)
            .GroupBy(binding => binding.Mouse)
            .ToFrozenDictionary(group => group.Key, group => group.Select(b => b.Action).Distinct().ToArray());
    }

    public static CompiledKeyboardMouseMap Default { get; } = KeyboardMouseMap.Default.Compile();

    public KeyboardMouseMap Source { get; }

    public FrozenDictionary<int, GamepadAction[]> KeyActions { get; }

    public FrozenDictionary<MouseInput, GamepadAction[]> MouseActions { get; }

    public GamepadAction[] ActionsFor(int virtualKey) =>
        KeyActions.TryGetValue(virtualKey, out var actions) ? actions : NoActions;

    public GamepadAction[] ActionsFor(MouseInput input) =>
        MouseActions.TryGetValue(input, out var actions) ? actions : NoActions;

    /// <summary>Legend for a keycap: the short names of everything that key is bound to, or null.</summary>
    public string? KeyHint(int virtualKey) =>
        KeyActions.TryGetValue(virtualKey, out var actions) && actions.Length > 0
            ? string.Join(" ", actions.Select(KeyboardMouseMap.ActionShortLabel))
            : null;

    /// <summary>Legend for a mouse button, or an empty string when it is unbound.</summary>
    public string MouseHint(MouseInput input) =>
        MouseActions.TryGetValue(input, out var actions) && actions.Length > 0
            ? string.Join(" ", actions.Select(KeyboardMouseMap.ActionShortLabel))
            : string.Empty;

    /// <summary>One line naming what mouse movement does, for the mouse picture.</summary>
    public string MotionHint => Source.MouseMotion switch
    {
        MouseMotionTarget.LeftStick => "Movement → left stick",
        MouseMotionTarget.RightStick => "Movement → right stick (aim)",
        _ => "Movement is not used",
    };
}
