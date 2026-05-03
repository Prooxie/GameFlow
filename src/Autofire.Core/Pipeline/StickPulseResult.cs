using Autofire.Core.Models;

namespace Autofire.Core.Pipeline;

public readonly record struct StickPulseResult(StickVector Value, bool ForceCenter);
