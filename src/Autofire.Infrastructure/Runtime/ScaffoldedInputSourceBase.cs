using Autofire.Core.Models;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime;

/// <summary>
/// Common base for input-source scaffolds whose underlying native
/// integration is not yet wired up. Behaves like
/// <see cref="NullInputSource"/> at runtime — clears the device
/// catalog with a clear "Requires X — not yet operational" reason,
/// returns empty snapshots on every read tick, and logs once on
/// construction so the operator sees what's missing.
///
/// <para>
/// Concrete subclasses exist as named files in their respective
/// driver-specific folders (e.g. <c>VJoy/</c>, <c>X360ce/</c>) so
/// that when someone is ready to add real PInvoke / native interop
/// the right place is obvious. The runtime path is fully hooked
/// up — only the body needs filling in.
/// </para>
/// </summary>
public abstract class ScaffoldedInputSourceBase : IInputSource
{
    private readonly InputDeviceCatalog inputDeviceCatalog;
    private readonly string reason;

    /// <summary>
    /// Constructs the scaffold. <paramref name="displayName"/> is what
    /// the runtime/UI shows in diagnostics (typically the provider's
    /// product name); <paramref name="requirementSummary"/> is the
    /// short human-readable explanation of what's missing
    /// (e.g. "Requires vJoy device driver — not yet operational.").
    /// </summary>
    protected ScaffoldedInputSourceBase(
        InputDeviceCatalog inputDeviceCatalog,
        ILogger logger,
        string displayName,
        string requirementSummary)
    {
        this.inputDeviceCatalog = inputDeviceCatalog;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Scaffolded input" : displayName.Trim();
        reason = string.IsNullOrWhiteSpace(requirementSummary)
            ? "This input source is scaffolded but not yet operational."
            : requirementSummary.Trim();

        logger.LogWarning(
            "{Provider} input source is scaffolded but not yet operational — falling back to no-op. Reason: {Reason}",
            DisplayName, reason);

        inputDeviceCatalog.Clear(reason);
    }

    public string DisplayName { get; }

    /// <inheritdoc />
    public ValueTask<ControllerSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        inputDeviceCatalog.SetProviderStatus(reason);
        return ValueTask.FromResult(ControllerSnapshot.Empty(reason));
    }

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync()
    {
        inputDeviceCatalog.Clear(reason);
        return ValueTask.CompletedTask;
    }
}
