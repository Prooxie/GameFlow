using GameFlow.Core.Models;

namespace GameFlow.Core.Pipeline;

public sealed record ControllerFrameResult(
    ControllerSnapshot PhysicalSnapshot,
    ControllerSnapshot VirtualSnapshot,
    IReadOnlyList<string> Notes);
