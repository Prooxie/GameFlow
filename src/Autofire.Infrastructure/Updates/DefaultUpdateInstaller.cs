using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Autofire.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autofire.Infrastructure.Updates;

/// <summary>
/// Default <see cref="IUpdateInstaller"/>. See the interface doc for
/// the rationale on why we don't auto-replace the install.
///
/// <para>
/// The downloaded asset lands in
/// <c>%TEMP%/AutofireNext-update/{filename}</c> on Windows, and the
/// platform equivalent on Linux / macOS. The file is then revealed in
/// the OS file manager (Explorer / Finder / xdg-open) and the release
/// notes URL is opened in the user's browser.
/// </para>
/// </summary>
public sealed class DefaultUpdateInstaller : IUpdateInstaller
{
    /// <summary>How long the whole download is allowed to take.</summary>
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

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

        // Always open the release notes — regardless of whether the
        // asset download succeeds, the user knows what changed.
        TryOpenInBrowser(update.ReleaseNotesUrl);

        if (update.DownloadAssetUrl is null || string.IsNullOrEmpty(update.DownloadAssetName))
        {
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
    private async Task<string> DownloadAssetAsync(
        UpdateInfo update,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var targetDir = Path.Combine(Path.GetTempPath(), "AutofireNext-update");
        _ = Directory.CreateDirectory(targetDir);

        var targetPath = Path.Combine(targetDir, update.DownloadAssetName!);
        var partialPath = targetPath + ".partial";

        using var client = new HttpClient(new SocketsHttpHandler
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

        try
        {
            await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = new FileStream(
                partialPath,
                FileMode.Create,
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
                    readSoFar += read;

                    if (progress is not null && totalBytes is > 0)
                    {
                        progress.Report(Math.Clamp((double)readSoFar / totalBytes.Value, 0.0, 1.0));
                    }
                }

                await fileStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            // Atomic-ish rename: only commit the final filename once
            // the bytes are fully on disk.
            if (File.Exists(targetPath))
            {
                File.Delete(targetPath);
            }
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
