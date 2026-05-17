namespace Autofire.Infrastructure.Updates;

/// <summary>
/// Description of a single available release.
/// </summary>
/// <param name="Version">
/// Parsed semver of the release tag (with the leading <c>v</c> stripped).
/// E.g. tag <c>v1.2.3</c> becomes <c>Version(1, 2, 3)</c>. Used for
/// comparison against the running assembly version.
/// </param>
/// <param name="TagName">
/// The raw tag string as returned by GitHub (e.g. <c>"v1.2.3"</c>). Kept
/// because the user-visible "skip this update" preference is keyed on
/// the exact tag string, not the parsed version.
/// </param>
/// <param name="ReleaseName">
/// The display name of the release as set by the publisher (often the
/// same as the tag, sometimes a friendly title like
/// <c>"v1.2.3 — quality-of-life pass"</c>).
/// </param>
/// <param name="ReleaseNotesUrl">
/// HTTPS URL of the release notes page. Shown as a link in the
/// "update available" dialog so the user can read what's changing.
/// </param>
/// <param name="DownloadAssetUrl">
/// HTTPS URL of the platform-specific asset to download. May be
/// <see langword="null"/> if no asset matches the running platform —
/// the dialog falls back to <see cref="ReleaseNotesUrl"/> in that case.
/// </param>
/// <param name="DownloadAssetName">
/// File name of the asset (used as a download filename and shown in
/// progress UI). May be <see langword="null"/> when
/// <see cref="DownloadAssetUrl"/> is.
/// </param>
public sealed record UpdateInfo(
    Version Version,
    string TagName,
    string ReleaseName,
    Uri ReleaseNotesUrl,
    Uri? DownloadAssetUrl,
    string? DownloadAssetName);
