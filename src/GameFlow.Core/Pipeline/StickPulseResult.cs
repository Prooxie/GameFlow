using GameFlow.Core.Models;

namespace GameFlow.Core.Pipeline;

public readonly record struct StickPulseResult(StickVector Value, bool ForceCenter);
