using GameFlow.Core.Enums;

namespace GameFlow.Core.Models.Rules;

public sealed record StickThresholdRule : MappingRule
{
    public StickId TargetStick { get; init; } = StickId.Right;
    public float Deadzone { get; init; } = 0.25f;
    public float FullAt { get; init; } = 0.90f;
    /// <summary>
    /// "Suppress raw source stick after shaping": the shaped stick still
    /// feeds the rules that read a stick as a SOURCE (stick autofire,
    /// freeze last direction, stick trim), but the stick itself reaches
    /// the game centred. This is how a stick becomes purely an input for
    /// other rules — e.g. the right stick driving left-stick autofire —
    /// without also turning the camera. It was saved and shown for a long
    /// time without the pipeline ever reading it.
    /// </summary>
    public bool SuppressSourceStick { get; init; }
}
