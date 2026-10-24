using GameFlow.Core.Enums;
using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Input;

/// <summary>Geometry shared by the native input pictures and the OBS canvas.</summary>
public sealed record InputSurfaceElement(
    string Kind, double X, double Y, double Width, double Height, double Radius,
    string Label = "", string? Hint = null, bool Bound = false,
    int Key = 0, MouseInput Mouse = MouseInput.None, MouseButtonFlags Flag = MouseButtonFlags.None);

public sealed record InputSurfaceLayout(double Width, double Height, IReadOnlyList<InputSurfaceElement> Elements)
{
    public const string Surface = "#161d26";
    public const string Body = "#202a36";
    public const string Idle = "#2b3644";
    public const string Border = "#4b5c70";
    public const string Foreground = "#edf2f7";
    public const string Accent = "#8cc8ff";
    public const string Active = "#65d384";

    public static bool Supports(ControllerVisualStyle style) =>
        style is ControllerVisualStyle.Keyboard or ControllerVisualStyle.Mouse or ControllerVisualStyle.KeyboardMouse;

    public static InputSurfaceLayout Create(ControllerVisualStyle style,
        CompiledKeyboardMouseMap keyboard, CompiledKeyboardMouseMap mouse, string? motionHint = null,
        Func<GamepadAction, string>? shortLabel = null)
    {
        if (!Supports(style)) { throw new ArgumentOutOfRangeException(nameof(style)); }
        var elements = new List<InputSurfaceElement>();
        var hasKeyboard = style != ControllerVisualStyle.Mouse;
        var hasMouse = style != ControllerVisualStyle.Keyboard;
        var height = hasMouse ? 300 : 234;
        var width = hasKeyboard ? 800 : 280;
        if (hasKeyboard && hasMouse) { width += 296; }

        if (hasKeyboard)
        {
            var top = (height - 234) / 2d;
            elements.Add(new("panel", 0, top, 800, 234, 10));
            foreach (var key in KeyboardPhysicalLayout.Ansi104)
            {
                var actions = keyboard.ActionsFor(key.VirtualKey);
                var bound = actions.Length > 0
                    ? string.Join(" ", actions.Select(shortLabel ?? KeyboardMouseMap.ActionShortLabel)) : null;
                elements.Add(new("key", key.X + 15, key.Y + 13 + top, key.Width, key.Height, 4,
                    key.Label, bound ?? key.Hint, bound is not null, Key: key.VirtualKey));
            }
        }

        if (hasMouse)
        {
            var left = hasKeyboard ? 816 : 0;
            elements.Add(new("panel", left, 0, 280, 300, 10));
            elements.Add(new("body", left + 50, 10, 180, 253, 65));
            Add(MouseInput.Left, MouseButtonFlags.Left, 57, 19, 69, 88, "1");
            Add(MouseInput.Right, MouseButtonFlags.Right, 154, 19, 69, 88, "2");
            Add(MouseInput.Middle, MouseButtonFlags.Middle, 128, 41, 24, 35, "3");
            Add(MouseInput.WheelUp, MouseButtonFlags.WheelUp, 128, 20, 24, 18, "▲");
            Add(MouseInput.WheelDown, MouseButtonFlags.WheelDown, 128, 79, 24, 18, "▼");
            Add(MouseInput.Back, MouseButtonFlags.Back, 28, 120, 34, 40, "4");
            Add(MouseInput.Forward, MouseButtonFlags.Forward, 28, 164, 34, 40, "5");
            elements.Add(new("motion", left + 110, 145, 60, 60, 0));
            elements.Add(new("label", left + 6, 270, 268, 24, 0, motionHint ?? mouse.MotionHint));

            void Add(MouseInput input, MouseButtonFlags flag, double x, double y, double w, double h, string label)
            {
                var hint = string.Join(" ", mouse.ActionsFor(input).Select(shortLabel ?? KeyboardMouseMap.ActionShortLabel));
                elements.Add(new("button", left + x, y, w, h, 6, label,
                    hint, hint.Length > 0, Mouse: input, Flag: flag));
            }
        }
        return new(width, height, elements);
    }
}
