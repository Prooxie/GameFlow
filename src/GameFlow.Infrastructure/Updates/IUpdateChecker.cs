namespace GameFlow.Infrastructure.Updates;

/// <summary>
/// Asks the upstream release server (GitHub by default) whether a
/// newer release than the running build is available.
///
/// <para>
/// Implementations must be safe to invoke multiple times without
/// side-effects. They MUST NOT throw — every failure mode is reported
/// through <see cref="UpdateCheckResult.Failed"/> so the caller can
/// decide whether to log and continue, retry, or surface.
/// </para>
/// </summary>
public interface IUpdateChecker
{
    /// <summary>
    /// Performs the update check.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation for the network call. The implementation honours
    /// this and returns <see cref="UpdateCheckResult.Failed"/> on
    /// cancellation rather than propagating, so callers can fire-and-
    /// forget.
    /// </param>
    Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}
