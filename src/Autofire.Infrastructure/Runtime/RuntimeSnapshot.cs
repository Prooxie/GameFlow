using Autofire.Core.Models;

namespace Autofire.Infrastructure.Runtime;

public sealed record RuntimeSnapshot
{
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;
    public string InputProvider { get; init; } = "initializing";
    public string OutputProvider { get; init; } = "preview";
    public ControllerSnapshot PhysicalSnapshot { get; init; } = ControllerSnapshot.Empty("Waiting for input provider");
    public ControllerSnapshot VirtualSnapshot { get; init; } = ControllerSnapshot.Empty("Waiting for output preview");
    public IReadOnlyList<string> Notes { get; init; } = [];
}
