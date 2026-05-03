using Autofire.Core.Models;

namespace Autofire.Core.Pipeline;

public sealed record ControllerFrameResult(
    ControllerSnapshot PhysicalSnapshot,
    ControllerSnapshot VirtualSnapshot,
    IReadOnlyList<string> Notes);
