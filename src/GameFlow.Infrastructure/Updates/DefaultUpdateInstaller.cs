using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using GameFlow.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameFlow.Infrastructure.Updates;

/// <summary>
/// Default <see cref="IUpdateInstaller"/>. See the interface doc for
/// the rationale on why we don't auto-replace the install.
///
/// <para>
/// The downloaded asset lands in
/// <c>%TEMP%/GAMEFLOW-update/{filename}</c> on Windows, and the
/// platform equivalent on Linux / macOS. The file is then revealed in
/// the OS file manager (Explorer / Finder / xdg-open) and the release
/// notes URL is opened in the user's browser.
/// </para>
/// </summary>
public sealed class DefaultUpdateInstaller : IUpdateInstaller
{
    /// <summary>How long the whole download is allowed to take.</summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);
    private const long MaxDownloadBytes = 1024L * 1024 * 1024;

    private readonly UpdateOptions options;
    private readonly ILogger<DefaultUpdateInstaller> logger;

    /// <summary>
    /// Constructs the installer.
    /// </summary>
    public DefaultUpdateInstaller(
        IOptions<AppRuntimeOptions> runtimeOptions,
        ILogger<DefaultUpdateInstaller> logger)
    {
        ArgumentNullException.ThrowIfNull(runtimeOptions);
        options = runtimeOptions.Value.Updates ?? new UpdateOptions();
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> DownloadAndRevealAsync(
        UpdateInfo update,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        if (!UpdateDownloadPolicy.IsHttps(update.ReleaseNotesUrl) ||
            (update.DownloadAssetUrl is not null && !UpdateDownloadPolicy.IsHttps(update.DownloadAssetUrl)) ||
            (update.DownloadAssetName is not null && !UpdateDownloadPolicy.IsSafeFileName(update.DownloadAssetName)))
        {
            logger.LogWarning("Update metadata contains an unsafe URL or filename; update rejected.");
            return false;
        }

        if (update.DownloadAssetUrl is null || string.IsNullOrEmpty(update.DownloadAssetName))
        {
            TryOpenInBrowser(update.ReleaseNotesUrl);
            logger.LogInformation(
                "Update {TagName} has no asset for the current platform; opened release notes only.",
                update.TagName);
            return false;
        }

        string targetPath;
        try
        {
            targetPath = await DownloadAssetAsync(update, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("Update download cancelled by the user.");
            return false;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Update download failed for {AssetName}.",
                update.DownloadAssetName);
            return false;
        }

        TryRevealInFileManager(targetPath);
        logger.LogInformation(
            "Update {TagName} downloaded to {TargetPath} and revealed in the file manager.",
            update.TagName,
            targetPath);
        return true;
    }

    /// <summary>
    /// Downloads the asset to a fresh temp folder. Streams to disk so
    /// large self-contained zips don't pin a hundred megabytes in RAM.
    /// </summary>
    internal async Task<string> DownloadAssetAsync(
        UpdateInfo update,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        HttpClient? httpClient = null)
    {
        if (update.DownloadAssetUrl is null || !UpdateDownloadPolicy.IsHttps(update.DownloadAssetUrl) ||
            update.DownloadAssetName is null || !UpdateDownloadPolicy.IsSafeFileName(update.DownloadAssetName))
        { throw new InvalidDataException("The update asset URL or filename is invalid."); }
        if (update.ExpectedSha256 is not { Length: 64 } hash || !hash.All(Uri.IsHexDigit))
        { throw new InvalidDataException("This release has no SHA-256 digest. Download it manually from the release page."); }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(DownloadTimeout);
        cancellationToken = deadline.Token;
        // Isolate attempts; never truncate another download or follow a pre-created partial file.
        var targetDir = Directory.CreateTempSubdirectory("GAMEFLOW-update-").FullName;

        var targetPath = Path.Combine(targetDir, update.DownloadAssetName!);
        var partialPath = targetPath + ".partial";

        using var client = httpClient ?? new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.None,
        })
        {
            Timeout = DownloadTimeout,
        };

        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        logger.LogInformation(
            "Downloading update asset {AssetName} from {Url} to {TargetPath}.",
            update.DownloadAssetName,
            update.DownloadAssetUrl,
            targetPath);

        using var response = await client.GetAsync(
            update.DownloadAssetUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        _ = response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        if (totalBytes > MaxDownloadBytes)
        {
            throw new HttpRequestException("Update archive exceeds the 1 GiB download limit.");
        }

        try
        {
            using var checksum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                var buffer = new byte[81920];
                long readSoFar = 0;
                int read;
                while ((read = await sourceStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    checksum.AppendData(buffer, 0, read);
                    readSoFar += read;
                    if (readSoFar > MaxDownloadBytes)
                    {
                        throw new HttpRequestException("Update archive exceeds the 1 GiB download limit.");
                    }

                    if (progress is not null && totalBytes is > 0)
                    {
                        progress.Report(Math.Clamp((double)readSoFar / totalBytes.Value, 0.0, 1.0));
                    }
                }

                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                if (totalBytes is { } expectedLength && expectedLength != readSoFar)
                { throw new InvalidDataException("The update archive is incomplete."); }
            }

            if (!CryptographicOperations.FixedTimeEquals(checksum.GetHashAndReset(), Convert.FromHexString(hash)))
            { throw new InvalidDataException("The update archive failed SHA-256 verification. It was not saved."); }

            // Atomic-ish rename: only commit the final filename once
            // the bytes are fully on disk.
            File.Move(partialPath, targetPath);

            progress?.Report(1.0);
            return targetPath;
        }
        catch
        {
            // Clean up partial files on any failure (including cancel).
            try
            {
                if (File.Exists(partialPath))
                {
                    File.Delete(partialPath);
                }
            }
            catch (Exception cleanupException)
            {
                logger.LogDebug(cleanupException, "Failed to remove partial download {PartialPath}.", partialPath);
            }
            throw;
        }
    }

    /// <summary>
    /// Asks the OS to open the file manager pointing at the file. On
    /// Windows uses <c>explorer.exe /select,...</c>; on macOS uses
    /// <c>open -R</c>; on Linux opens the parent directory via
    /// <c>xdg-open</c> (no portable "select" exists).
    /// </summary>
    private void TryRevealInFileManager(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{filePath}\"",
                    UseShellExecute = true,
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"-R \"{filePath}\"",
                    UseShellExecute = false,
                });
            }
            else
            {
                var directory = Path.GetDirectoryName(filePath) ?? Path.GetTempPath();
                _ = Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{directory}\"",
                    UseShellExecute = false,
                });
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not reveal {FilePath} in the file manager.", filePath);
        }
    }

    /// <summary>
    /// Opens the URL in the user's default browser. Best-effort —
    /// failures are logged at Debug and swallowed so they don't block
    /// the rest of the update flow.
    /// </summary>
    private void TryOpenInBrowser(Uri url)
    {
        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = url.ToString(),
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not open {Url} in the browser.", url);
        }
    }
}
