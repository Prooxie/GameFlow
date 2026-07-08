using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime;

/// <summary>
/// Contract for any sink the runtime can write virtual controller snapshots to.
/// </summary>
public interface IOutputSink : IAsyncDisposable
{
    /// <summary>Human-readable name (used in diagnostics and the dashboard).</summary>
    string DisplayName { get; }

    /// <summary>
    /// (Vendor id, Product id) of the virtual device this sink advertises to the
    /// host OS, when applicable. Returned non-null by sinks that emit a real
    /// virtual device through a kernel driver (e.g. ViGEm Bus): the runtime then
    /// asks the input device catalog to hide any input device matching the same
    /// signature, so the user does not see the runtime's own output device in
    /// the input source dropdown.
    ///
    /// Returns null for sinks that don't materialise as an OS-visible device
    /// (mock/no-op sinks, log-only sinks, etc.) — those don't need filtering.
    /// </summary>
    (ushort Vid, ushort Pid)? OwnedHardwareSignature => null;

    /// <summary>
    /// Pushes a virtual controller snapshot to the underlying device.
    /// Implementations should be cheap to call at the runtime tick rate.
    /// </summary>
    /// <param name="snapshot">The virtual controller state to write.</param>
    /// <param name="cancellationToken">Cancellation token for the runtime tick.</param>
    /// <returns>A task that completes when the snapshot has been queued.</returns>
    ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken);
}
