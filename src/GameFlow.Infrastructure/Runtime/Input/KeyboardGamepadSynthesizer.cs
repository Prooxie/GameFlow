using GameFlow.Core.Enums;
using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Input;

/// <summary>
/// Pure transform: given the set of Windows virtual-key codes currently
/// down on a keyboard, produce the equivalent <see cref="ControllerSnapshot"/>
/// per a <see cref="CompiledKeyboardMouseMap"/>. Sticks accumulate from key
/// directions and clamp to unit magnitude (diagonals don't exceed 1);
/// triggers are digital (1.0 when a bound key is down).
/// </summary>
public static class KeyboardGamepadSynthesizer
{
    /// <summary>Synthesizes with the built-in default bindings.</summary>
    public static ControllerSnapshot Synthesize(string deviceName, IReadOnlySet<int> pressedVirtualKeys) =>
        Synthesize(deviceName, pressedVirtualKeys, CompiledKeyboardMouseMap.Default);

    public static ControllerSnapshot Synthesize(
        string deviceName,
        IReadOnlySet<int> pressedVirtualKeys,
        CompiledKeyboardMouseMap map)
    {
        ArgumentNullException.ThrowIfNull(pressedVirtualKeys);
        ArgumentNullException.ThrowIfNull(map);

        var accumulator = new GamepadActionAccumulator();
        foreach (var vk in pressedVirtualKeys)
        {
            foreach (var action in map.ActionsFor(vk))
            {
                accumulator.Apply(action);
            }
        }

        return accumulator.ToSnapshot(deviceName) with { PressedKeys = pressedVirtualKeys };
    }
}

/// <summary>
/// Folds a set of held <see cref="GamepadAction"/>s into stick, trigger and
/// button state. Shared by the keyboard and mouse synthesizers so the same
/// action means the same thing whichever device produced it.
/// </summary>
internal struct GamepadActionAccumulator
{
    private float lx, ly, rx, ry, lt, rt;
    private bool walk;
    private ButtonMask buttons;

    public GamepadActionAccumulator()
    {
        buttons = ButtonMask.Empty;
    }

    public void Apply(GamepadAction action)
    {
        switch (action)
        {
            case GamepadAction.LeftStickUp:    ly += 1f; break;
            case GamepadAction.LeftStickDown:  ly -= 1f; break;
            case GamepadAction.LeftStickLeft:  lx -= 1f; break;
            case GamepadAction.LeftStickRight: lx += 1f; break;
            case GamepadAction.RightStickUp:    ry += 1f; break;
            case GamepadAction.RightStickDown:  ry -= 1f; break;
            case GamepadAction.RightStickLeft:  rx -= 1f; break;
            case GamepadAction.RightStickRight: rx += 1f; break;
            case GamepadAction.LeftTriggerFull:  lt = 1f; break;
            case GamepadAction.RightTriggerFull: rt = 1f; break;
            case GamepadAction.LeftStickWalk:    walk = true; break;
            case GamepadAction.South:         buttons[ButtonId.South] = true; break;
            case GamepadAction.East:          buttons[ButtonId.East] = true; break;
            case GamepadAction.West:          buttons[ButtonId.West] = true; break;
            case GamepadAction.North:         buttons[ButtonId.North] = true; break;
            case GamepadAction.LeftShoulder:  buttons[ButtonId.LeftShoulder] = true; break;
            case GamepadAction.RightShoulder: buttons[ButtonId.RightShoulder] = true; break;
            case GamepadAction.Back:          buttons[ButtonId.Back] = true; break;
            case GamepadAction.Start:         buttons[ButtonId.Start] = true; break;
            case GamepadAction.Guide:         buttons[ButtonId.Guide] = true; break;
            case GamepadAction.DpadUp:        buttons[ButtonId.DpadUp] = true; break;
            case GamepadAction.DpadDown:      buttons[ButtonId.DpadDown] = true; break;
            case GamepadAction.DpadLeft:      buttons[ButtonId.DpadLeft] = true; break;
            case GamepadAction.DpadRight:     buttons[ButtonId.DpadRight] = true; break;
            case GamepadAction.LeftStickClick:  buttons[ButtonId.LeftStick] = true; break;
            case GamepadAction.RightStickClick: buttons[ButtonId.RightStick] = true; break;
            case GamepadAction.Touchpad:      buttons[ButtonId.Touchpad] = true; break;
            case GamepadAction.Misc:          buttons[ButtonId.Misc1] = true; break;
        }
    }

    public readonly ControllerSnapshot ToSnapshot(string deviceName)
    {
        var left = ClampToUnit(lx, ly);
        if (walk)
        {
            left = new StickVector(left.X * 0.5f, left.Y * 0.5f);
        }

        // A digital trigger also closes the trigger's button, the way a
        // real pad's trigger reports both once it is fully pulled.
        var result = buttons;
        if (lt > 0f) { result[ButtonId.LeftTriggerButton] = true; }
        if (rt > 0f) { result[ButtonId.RightTriggerButton] = true; }

        return new ControllerSnapshot
        {
            DeviceName   = deviceName,
            LeftStick    = left,
            RightStick   = ClampToUnit(rx, ry),
            LeftTrigger  = lt,
            RightTrigger = rt,
            Buttons      = result,
        };
    }

    private static StickVector ClampToUnit(float x, float y)
    {
        var magnitude = MathF.Sqrt(x * x + y * y);
        return magnitude > 1f
            ? new StickVector(x / magnitude, y / magnitude)
            : new StickVector(x, y);
    }
}
