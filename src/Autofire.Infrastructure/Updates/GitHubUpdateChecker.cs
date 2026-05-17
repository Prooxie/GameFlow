using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Autofire.Infrastructure.Configuration;
using Autofire.Infrastructure.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autofire.Infrastructure.Updates;

/// <summary>
/// <see cref="IUpdateChecker"/> implementation that queries the GitHub
/// REST API for the latest release of the configured repository.
///
/// <para>
/// Hits <c>https://api.github.com/repos/{owner}/{repo}/releases/latest</c>
/// (no auth required for public repos), parses the JSON, picks the
/// right asset for the running platform's runtime identifier, and
/// returns an <see cref="UpdateCheckResult"/>.
/// </para>
///
/// <para>
/// Reliability: this method is called at startup and the user is not
/// blocked on it. Every failure path (no network, rate limit, malformed
/// JSON, missing config) is collapsed into
/// <see cref="UpdateCheckResult.Failed"/> so the caller can ignore it
/// and let the user keep using the app.
/// </para>
/// </summary>
public sealed class GitHubUpdateChecker : IUpdateChecker
{
    /// <summary>How long any single network call is allowed to take.</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The minimum semver we'll consider a "real" version. v0.0.0 means we couldn't read the assembly version.</summary>
    private static readonly Version FallbackVersion = new(0, 0, 0);

    private readonly UpdateOptions options;
    private readonly IUserSettingsService userSettings;
    private readonly ILogger<GitHubUpdateChecker> logger;

    /// <summary>
    /// Constructs the checker.
    /// </summary>
    public GitHubUpdateChecker(
        IOptions<AppRuntimeOptions> runtimeOptions,
        IUserSettingsService userSettings,
        ILogger<GitHubUpdateChecker> logger)
    {
        ArgumentNullException.ThrowIfNull(runtimeOptions);
        options = runtimeOptions.Value.Updates ?? new UpdateOptions();
        this.userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var settings = userSettings.Current;

        if (!settings.CheckForUpdatesOnStartup)
        {
            logger.LogDebug("Update check skipped: CheckForUpdatesOnStartup is disabled.");
            return new UpdateCheckResult.Disabled();
        }

        if (string.IsNullOrWhiteSpace(options.RepoOwner) || string.IsNullOrWhiteSpace(options.RepoName))
        {
            logger.LogInformation(
                "Update check skipped: Runtime:Updates:RepoOwner / RepoName are not configured in appsettings.json.");
            return new UpdateCheckResult.Failed("Updater is not configured.", null);
        }

        var currentVersion = ReadCurrentVersion();

        UpdateInfo? update;
        try
        {
            update = await FetchLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug("Update check cancelled.");
            return new UpdateCheckResult.Failed("Cancelled.", null);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Update check failed.");
            return new UpdateCheckResult.Failed(exception.Message, exception);
        }

        if (update is null)
        {
            return new UpdateCheckResult.Failed("GitHub returned no release data.", null);
        }

        if (update.Version <= currentVersion)
        {
            logger.LogInformation(
                "Update check: running {CurrentVersion}, latest is {LatestVersion} — up to date.",
                currentVersion,
                update.Version);
            return new UpdateCheckResult.UpToDate(currentVersion);
        }

        // Honour an earlier "skip this update" choice — the user pinned a
        // specific tag, not a version, so compare the tag string.
        if (!string.IsNullOrWhiteSpace(settings.SkippedUpdateVersion) &&
            string.Equals(settings.SkippedUpdateVersion, update.TagName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Update {TagName} is available but the user previously chose to skip it.",
                update.TagName);
            return new UpdateCheckResult.SkippedByUser(currentVersion, update);
        }

        logger.LogInformation(
            "Update available: running {CurrentVersion}, latest is {LatestVersion} ({TagName}).",
            currentVersion,
            update.Version,
            update.TagName);
        return new UpdateCheckResult.Available(currentVersion, update);
    }

    /// <summary>
    /// Reads the running assembly's <see cref="System.Reflection.AssemblyName.Version"/>
    /// and downcasts to a 3-component <see cref="Version"/>. Falls back
    /// to <c>0.0.0</c> when the version is missing — that means our
    /// comparison treats every published release as "newer", which is
    /// the safe direction.
    /// </summary>
    private static Version ReadCurrentVersion()
    {
        var entry = Assembly.GetEntryAssembly()?.GetName().Version;
        if (entry is null)
        {
            return FallbackVersion;
        }

        return new Version(entry.Major, entry.Minor, Math.Max(entry.Build, 0));
    }

    /// <summary>
    /// Performs the HTTP call and parses the response into an
    /// <see cref="UpdateInfo"/>. Returns <see langword="null"/> when
    /// GitHub responds with a body we can't parse but no exception.
    /// </summary>
    private async Task<UpdateInfo?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        // One client per call is fine here — startup-only, no concurrency.
        using var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        })
        {
            Timeout = RequestTimeout,
        };

        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var url = $"https://api.github.com/repos/{options.RepoOwner}/{options.RepoName}/releases/latest";
        logger.LogDebug("GET {Url}", url);

        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "GitHub returned {StatusCode} for {Url}.",
                (int)response.StatusCode,
                url);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return ParseRelease(stream);
    }

    /// <summary>
    /// Parses the GitHub <c>/releases/latest</c> JSON body into an
    /// <see cref="UpdateInfo"/>. Returns <see langword="null"/> on any
    /// parse failure rather than throwing — failures are logged at
    /// Warning so support has a trail.
    /// </summary>
    private UpdateInfo? ParseRelease(Stream stream)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Could not parse GitHub release JSON.");
            return null;
        }

        using (document)
        {
            var root = document.RootElement;

            if (!TryGetString(root, "tag_name", out var tagName))
            {
                logger.LogWarning("GitHub release JSON has no tag_name.");
                return null;
            }

            var version = ParseTagVersion(tagName);
            if (version is null)
            {
                logger.LogWarning("Could not parse semver from tag {TagName}.", tagName);
                return null;
            }

            _ = TryGetString(root, "name", out var releaseName);
            _ = TryGetString(root, "html_url", out var htmlUrl);

            var releaseNotesUrl = !string.IsNullOrWhiteSpace(htmlUrl) && Uri.TryCreate(htmlUrl, UriKind.Absolute, out var parsedHtml)
                ? parsedHtml
                : new Uri($"https://github.com/{options.RepoOwner}/{options.RepoName}/releases/tag/{tagName}");

            var (assetUrl, assetName) = PickAsset(root, tagName);

            return new UpdateInfo(
                Version: version,
                TagName: tagName,
                ReleaseName: string.IsNullOrWhiteSpace(releaseName) ? tagName : releaseName!,
                ReleaseNotesUrl: releaseNotesUrl,
                DownloadAssetUrl: assetUrl,
                DownloadAssetName: assetName);
        }
    }

    /// <summary>
    /// Looks through the <c>assets</c> array for one whose name matches
    /// the configured pattern after substituting the tag and the
    /// current runtime identifier. Returns the first match.
    /// </summary>
    private (Uri? Url, string? Name) PickAsset(JsonElement root, string tagName)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return (null, null);
        }

        var rid = RuntimeInformation.RuntimeIdentifier;
        var preferredName = options.AssetNamePattern
            .Replace("{tag}", tagName, StringComparison.Ordinal)
            .Replace("{rid}", rid, StringComparison.Ordinal);

        // Try exact match first, then tail match (handles cases where
        // the pattern contains slight prefix differences).
        foreach (var asset in assets.EnumerateArray())
        {
            if (!TryGetString(asset, "name", out var name) ||
                !TryGetString(asset, "browser_download_url", out var downloadUrl))
            {
                continue;
            }

            if (string.Equals(name, preferredName, StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(downloadUrl, UriKind.Absolute, out var url))
            {
                return (url, name);
            }
        }

        // Fallback: pick the first asset whose name contains the RID
        // (e.g. "win-x64") if the strict pattern didn't match.
        foreach (var asset in assets.EnumerateArray())
        {
            if (!TryGetString(asset, "name", out var name) ||
                !TryGetString(asset, "browser_download_url", out var downloadUrl))
            {
                continue;
            }

            if (name.Contains(rid, StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(downloadUrl, UriKind.Absolute, out var url))
            {
                logger.LogDebug(
                    "Strict asset name {PreferredName} not found; using {AssetName} based on RID match.",
                    preferredName,
                    name);
                return (url, name);
            }
        }

        logger.LogInformation(
            "No release asset matched the configured pattern {Pattern} or the RID {Rid}.",
            preferredName,
            rid);
        return (null, null);
    }

    /// <summary>
    /// Strips a leading <c>v</c> from a tag and parses the rest as a
    /// 1- to 4-component <see cref="Version"/>. Returns
    /// <see langword="null"/> if parsing fails.
    /// </summary>
    private static Version? ParseTagVersion(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        var trimmed = tag.AsSpan().Trim();
        if (trimmed.Length > 0 && (trimmed[0] == 'v' || trimmed[0] == 'V'))
        {
            trimmed = trimmed[1..];
        }

        return Version.TryParse(trimmed, out var parsed) ? parsed : null;
    }

    /// <summary>
    /// Helper: reads a string property if present and non-null. Returns
    /// <see langword="false"/> on missing / wrong-kind / null-valued
    /// properties.
    /// </summary>
    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            var raw = property.GetString();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                value = raw;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }
}
