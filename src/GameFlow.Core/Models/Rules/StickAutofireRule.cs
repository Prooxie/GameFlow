using GameFlow.Core.Enums;

namespace GameFlow.Core.Models.Rules;

public sealed record StickAutofireRule : MappingRule
{
    public StickId SourceStick { get; init; } = StickId.Right;
    public StickId TargetStick { get; init; } = StickId.Left;
    public StickBlendMode BlendMode { get; init; } = StickBlendMode.Additive;
    public bool SuppressSourceStick { get; init; }
    public float ActivationDeadzone { get; init; } = 0.12f;
    public float ActivationFullAt { get; init; } = 0.90f;
    public PulseTimingOptions Timing { get; init; } = new();
}
