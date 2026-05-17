namespace Autofire.Infrastructure.Updates;

/// <summary>
/// Performs the user-accepted side of an update: downloads the
/// platform-specific asset and then hands off to the OS so the user
/// can finish the install themselves.
///
/// <para>
/// Autofire ships as a self-contained zip per the README ("Unzip
/// anywhere and run Autofire.App.exe"), so a fully-automated installer
/// would be both unnecessary and risky (corrupted in-place updates are
/// a classic auto-updater failure mode). Instead, the installer
/// downloads the asset to a temp folder and reveals the file in the
/// platform's file manager, then opens the release notes page so the
/// user knows what changed.
/// </para>
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>
    /// Downloads the asset described by <paramref name="update"/> and
    /// reveals it in the user's file manager. Falls back to opening the
    /// release-notes URL when no asset is available for the current
    /// platform.
    /// </summary>
    /// <param name="update">The update to install.</param>
    /// <param name="progress">
    /// Optional progress reporter. Reports values 0.0–1.0 during
    /// download. A final report of 1.0 fires only after the file is
    /// fully written and synced to disk.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation for the download. Cancellation deletes the partial
    /// file before returning.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the download and reveal both succeeded;
    /// <see langword="false"/> when the user has no asset available
    /// (release-notes URL was opened as a fallback) or the download
    /// failed (logged at Warning).
    /// </returns>
    Task<bool> DownloadAndRevealAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}
