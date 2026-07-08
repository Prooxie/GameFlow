namespace GameFlow.Infrastructure.Updates;

/// <summary>
/// Outcome of an <see cref="IUpdateChecker.CheckAsync"/> call.
///
/// <para>
/// Modelled as an abstract record with fixed-set subclasses (a
/// "discriminated union") so the orchestrating coordinator can pattern-
/// match exhaustively on the result.
/// </para>
/// </summary>
public abstract record UpdateCheckResult
{
    /// <summary>
    /// Update checking is disabled by user preference
    /// (<c>AppSettings.CheckForUpdatesOnStartup == false</c>). The
    /// checker should never have been called; this exists so the
    /// coordinator can short-circuit early without special-casing.
    /// </summary>
    public sealed record Disabled : UpdateCheckResult;

    /// <summary>
    /// The running assembly is up-to-date relative to the latest
    /// published release.
    /// </summary>
    public sealed record UpToDate(Version CurrentVersion) : UpdateCheckResult;

    /// <summary>
    /// A newer release is available and the user has not previously
    /// chosen to skip this exact tag.
    /// </summary>
    /// <param name="CurrentVersion">Version of the running assembly.</param>
    /// <param name="Update">Description of the new release.</param>
    public sealed record Available(Version CurrentVersion, UpdateInfo Update) : UpdateCheckResult;

    /// <summary>
    /// A newer release is available, but the user previously asked to
    /// skip this exact tag. The coordinator should not show the dialog;
    /// the value is reported so support logs and diagnostics still
    /// reflect that an update exists.
    /// </summary>
    public sealed record SkippedByUser(Version CurrentVersion, UpdateInfo Update) : UpdateCheckResult;

    /// <summary>
    /// The check itself failed (network down, GitHub API rate-limited,
    /// JSON malformed, etc.). The reason is human-readable and
    /// suitable for diagnostics; it is NOT shown directly to the user.
    /// </summary>
    public sealed record Failed(string Reason, Exception? Exception) : UpdateCheckResult;
}
