using Autofire.Core.Models;

namespace Autofire.Infrastructure.Runtime;

public sealed class NullInputSource : IInputSource
{
    private readonly InputDeviceCatalog inputDeviceCatalog;
    private readonly string reason;

    public NullInputSource(InputDeviceCatalog inputDeviceCatalog)
        : this(inputDeviceCatalog, "No live input", "Live input is disabled.")
    {
    }

    public NullInputSource(InputDeviceCatalog inputDeviceCatalog, string displayName, string reason)
    {
        this.inputDeviceCatalog = inputDeviceCatalog;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "No live input" : displayName.Trim();
        this.reason = string.IsNullOrWhiteSpace(reason) ? "Live input is disabled." : reason.Trim();
        inputDeviceCatalog.Clear(this.reason);
    }

    public string DisplayName { get; }

    public ValueTask<ControllerSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        inputDeviceCatalog.SetProviderStatus(reason);
        return ValueTask.FromResult(ControllerSnapshot.Empty(reason));
    }

    public ValueTask DisposeAsync()
    {
        inputDeviceCatalog.Clear(reason);
        return ValueTask.CompletedTask;
    }
}
