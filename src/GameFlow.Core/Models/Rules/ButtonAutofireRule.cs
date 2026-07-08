using GameFlow.Core.Enums;

namespace GameFlow.Core.Models.Rules;

public sealed record ButtonAutofireRule : MappingRule
{
    public ButtonId SourceButton { get; init; } = ButtonId.RightShoulder;
    public ButtonId TargetButton { get; init; } = ButtonId.South;
    public bool SuppressSourceButton { get; init; }
    public PulseTimingOptions Timing { get; init; } = new();
}
