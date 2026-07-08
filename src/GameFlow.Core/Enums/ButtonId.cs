namespace GameFlow.Core.Enums;

/// <summary>
/// Identifier of a single physical or virtual button on a controller.
///
/// <c>None</c> is the default-initialized value and acts as a sentinel meaning
/// "no button selected" — used by <c>ButtonComboRule.SourceButton</c>,
/// <c>ButtonComboStep.Button</c>, and the early-return guards in the pipeline
/// executors. It must remain the first declared value so that an uninitialised
/// <c>ButtonId</c> field defaults to <c>None</c> rather than a real button.
///
/// JSON serialization uses enum names (System.Text.Json default), so adding
/// <c>None</c> at the head of the list shifts ordinal values but does NOT
/// break existing profile JSON files.
/// </summary>
public enum ButtonId
{
    None,
    South,
    East,
    West,
    North,
    LeftShoulder,
    RightShoulder,
    LeftTriggerButton,
    RightTriggerButton,
    Back,
    Start,
    Guide,
    LeftStick,
    RightStick,
    DpadUp,
    DpadDown,
    DpadLeft,
    DpadRight,
    Paddle1,
    Paddle2,
    Paddle3,
    Paddle4,
    Touchpad,
    Misc1
}