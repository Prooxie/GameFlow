using Autofire.Core.Enums;

namespace Autofire.Core.Models.Rules;

public sealed record FreezeLastDirectionRule : MappingRule
{
    public ButtonId ActivationButton { get; init; } = ButtonId.LeftShoulder;
    public StickId CaptureStick { get; init; } = StickId.Right;
    public StickId TargetStick { get; init; } = StickId.Left;
    public StickBlendMode BlendMode { get; init; } = StickBlendMode.Additive;
    public bool SuppressActivationButton { get; init; }
    public bool SuppressCaptureStick { get; init; }
    public bool PulseEnabled { get; init; } = true;
    public PulseTimingOptions Timing { get; init; } = new();
}
