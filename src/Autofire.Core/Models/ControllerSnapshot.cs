using Autofire.Core.Enums;

namespace Autofire.Core.Models;

public sealed record ControllerSnapshot
{
    public string DeviceName { get; init; } = string.Empty;
    public StickVector LeftStick { get; init; } = StickVector.Zero;
    public StickVector RightStick { get; init; } = StickVector.Zero;
    public float LeftTrigger { get; init; }
    public float RightTrigger { get; init; }
    public int TouchContactCount { get; init; }
    public IReadOnlyDictionary<ButtonId, bool> Buttons { get; init; } = ButtonState.CreateEmptyMap();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public static ControllerSnapshot Empty(string? deviceName = null)
    {
        return new()
        {
            DeviceName = deviceName ?? string.Empty,
            Buttons = ButtonState.CreateEmptyMap(),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    public StickVector GetStick(StickId stickId)
    {
        return stickId == StickId.Left ? LeftStick : RightStick;
    }

    public bool IsPressed(ButtonId buttonId)
    {
        return Buttons.TryGetValue(buttonId, out var isPressed) && isPressed;
    }

    public ControllerSnapshot WithStick(StickId stickId, StickVector value)
    {
        return stickId == StickId.Left
                ? this with { LeftStick = value.Clamp() }
                : this with { RightStick = value.Clamp() };
    }

    public ControllerSnapshot WithButtons(IReadOnlyDictionary<ButtonId, bool> buttons)
    {
        return this with { Buttons = buttons };
    }

    public ControllerSnapshot WithTriggers(float leftTrigger, float rightTrigger)
    {
        return this with
        {
            LeftTrigger = Math.Clamp(leftTrigger, 0f, 1f),
            RightTrigger = Math.Clamp(rightTrigger, 0f, 1f)
        };
    }

    public ControllerSnapshot WithTouchContactCount(int touchContactCount)
    {
        return this with { TouchContactCount = Math.Max(0, touchContactCount) };
    }
}
