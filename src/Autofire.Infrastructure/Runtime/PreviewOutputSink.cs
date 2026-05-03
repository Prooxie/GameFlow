using Autofire.Core.Models;

namespace Autofire.Infrastructure.Runtime;

public sealed class PreviewOutputSink : IOutputSink
{
    public string DisplayName => "PreviewOutput";

    public ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken)
    {
        LastSnapshot = snapshot;
        return ValueTask.CompletedTask;
    }

    public ControllerSnapshot LastSnapshot { get; private set; } = ControllerSnapshot.Empty("preview");

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
