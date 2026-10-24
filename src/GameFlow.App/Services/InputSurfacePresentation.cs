using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Runtime.Input;

namespace GameFlow.App.Services;

internal static class InputSurfacePresentation
{
    public static InputSurfaceLayout Create(ControllerVisualStyle style, string? keyboardId, string? mouseId)
    {
        var keyboard = KeyboardMouseLegend.ForDevice(keyboardId);
        var mouse = KeyboardMouseLegend.ForDevice(mouseId);
        return InputSurfaceLayout.Create(style, keyboard, mouse,
            mouse.Source.MouseMotion switch
            {
                MouseMotionTarget.LeftStick => UiTextResources.Get("MotionCaption.LeftStick"),
                MouseMotionTarget.RightStick => UiTextResources.Get("MotionCaption.RightStick"),
                _ => UiTextResources.Get("MotionCaption.None")
            }, ShortLabel);
    }

    private static string ShortLabel(GamepadAction action) => action switch
    {
        GamepadAction.Back => UiTextResources.Get("ControlShort.Back"),
        GamepadAction.Start => UiTextResources.Get("ControlShort.Start"),
        GamepadAction.Guide => UiTextResources.Get("ControlShort.Guide"),
        GamepadAction.LeftStickWalk => UiTextResources.Get("ControlShort.LeftStickWalk"),
        GamepadAction.Touchpad => UiTextResources.Get("ControlShort.Touchpad"),
        GamepadAction.Misc => UiTextResources.Get("ControlShort.Misc"),
        _ => KeyboardMouseMap.ActionShortLabel(action)
    };
}
