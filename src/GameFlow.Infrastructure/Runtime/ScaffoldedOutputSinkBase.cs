using GameFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime;

/// <summary>
/// Common base for output-sink scaffolds whose underlying native
/// integration is not yet wired up. Logs once on construction and
/// silently no-ops every <see cref="WriteAsync"/> call — the runtime
/// stays alive, the dashboard's preview path keeps working, and the
/// operator sees a clear log line explaining what's missing.
///
/// <para>
/// Concrete subclasses exist as named files in their respective
/// driver-specific folders (e.g. <c>VJoy/</c>, <c>HidMaestro/</c>).
/// </para>
/// </summary>
public abstract class ScaffoldedOutputSinkBase : IOutputSink
{
    private readonly string reason;

    /// <summary>
    /// Constructs the scaffold. See
    /// the non-SDK HIDMaestro build for the one remaining use.
    /// </summary>
    protected ScaffoldedOutputSinkBase(
        ILogger logger,
        string displayName,
        string requirementSummary)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Scaffolded output" : displayName.Trim();
        reason = string.IsNullOrWhiteSpace(requirementSummary)
            ? "This output sink is scaffolded but not yet operational."
            : requirementSummary.Trim();

        logger.LogWarning(
            "{Provider} output sink is scaffolded but not yet operational — falling back to no-op. Reason: {Reason}",
            DisplayName, reason);
    }

    /// <inheritdoc />
    public string DisplayName { get; }

    /// <inheritdoc />
    public ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Intentional no-op: real subclasses will translate `snapshot`
        // into native API calls here.
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public virtual ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
