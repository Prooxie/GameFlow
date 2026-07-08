using GameFlow.Core.Enums;

namespace GameFlow.Core.Models.Rules;

public sealed record StickThresholdRule : MappingRule
{
    public StickId TargetStick { get; init; } = StickId.Right;
    public float Deadzone { get; init; } = 0.25f;
    public float FullAt { get; init; } = 0.90f;
    public bool SuppressSourceStick { get; init; }
}
