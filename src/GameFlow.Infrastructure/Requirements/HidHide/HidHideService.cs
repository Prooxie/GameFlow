using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using GameFlow.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace GameFlow.Infrastructure.Requirements.HidHide;

/// <summary>
/// Default <see cref="IHidHideService"/>. Installs one pinned HidHide release
/// from Nefarius' GitHub releases, never "latest": a new driver version is a
/// deliberate change, like the pinned HIDMaestro SDK.
/// </summary>
public sealed class HidHideService : IHidHideService
{
    /// <summary>Pinned HidHide release.</summary>
    public const string PinnedVersion = "1.5.230";

    /// <summary>Installer file name of <see cref="PinnedVersion"/>.</summary>
    public const string InstallerFileName = "HidHide_" + PinnedVersion + "_x64.exe";

    /// <summary>The official release asset.</summary>
    public static readonly Uri InstallerUrl =
        new($"https://github.com/nefarius/HidHide/releases/download/v{PinnedVersion}.0/{InstallerFileName}");

    /// <summary>Releases page, for a manual install.</summary>
    public static readonly Uri ReleasesUrl = new("https://github.com/nefarius/HidHide/releases");

    /// <summary>Text the Authenticode signer's name must contain.</summary>
    internal const string ExpectedPublisher = "Nefarius";

    private const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\HidHide";
    private const long MaxDownloadBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    private readonly IUserSettingsService settings;
    private readonly ILogger<HidHideService> logger;

    /// <summary>Constructs the service.</summary>
    public HidHideService(IUserSettingsService settings, ILogger<HidHideService> logger)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsSupported => OperatingSystem.IsWindows();

    /// <inheritdoc />
    public bool IsWanted => IsSupported && settings.Current.UseHidHide;

    /// <inheritdoc />
    public bool IsInstalled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(ServiceKey);
            return key is not null;
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            logger.LogWarning(exception, "Could not read the HidHide service key.");
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<HidHideInstallOutcome> InstallAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new(HidHideInstallResult.NotSupported, "HidHide is only available on Windows.");
        }

        if (IsInstalled())
        {
            var listed = await AllowListGameFlowAsync(cancellationToken).ConfigureAwait(false);
            return new(HidHideInstallResult.AlreadyInstalled, listed
                ? "HidHide is already installed. GameFlow is on its allowed-applications list."
                : "HidHide is already installed. Add GameFlow.exe to its allowed applications in the HidHide Configuration Client.");
        }

        string? directory = null;
        try
        {
            directory = Directory.CreateTempSubdirectory("GAMEFLOW-hidhide-").FullName;
            var installer = Path.Combine(directory, InstallerFileName);
            await DownloadAsync(installer, progress, cancellationToken).ConfigureAwait(false);

            var signer = AuthenticodeVerifier.VerifyAndGetSigner(installer, out var signatureError);
            if (signer is null || !AuthenticodeVerifier.IsExpectedPublisher(signer, ExpectedPublisher))
            {
                var reason = signatureError ?? $"it is signed by '{signer}', not by Nefarius";
                logger.LogWarning("HidHide installer rejected: {Reason}.", reason);
                return new(HidHideInstallResult.Failed, $"The downloaded HidHide installer was not run because {reason}.");
            }

            logger.LogInformation("HidHide installer {File} verified, signed by {Signer}. Starting it.", InstallerFileName, signer);
            var exitCode = await RunElevatedAsync(installer, arguments: null, cancellationToken).ConfigureAwait(false);
            var result = MapInstallerExitCode(exitCode, IsInstalled());
            logger.LogInformation("HidHide installer exited with {ExitCode}; result {Result}.", exitCode, result);

            if (result is HidHideInstallResult.Installed or HidHideInstallResult.RebootRequired)
            {
                var listed = await AllowListGameFlowAsync(cancellationToken).ConfigureAwait(false);
                var restart = result == HidHideInstallResult.RebootRequired
                    ? " Restart Windows before HidHide starts hiding controllers."
                    : string.Empty;
                return new(result, (listed
                    ? "HidHide is installed and GameFlow is on its allowed-applications list."
                    : "HidHide is installed. Add GameFlow.exe to its allowed applications in the HidHide Configuration Client.")
                    + restart + " Choose which controllers to hide in the HidHide Configuration Client.");
            }

            return result == HidHideInstallResult.Cancelled
                ? new(result, "The HidHide installation was cancelled.")
                : new(result, $"The HidHide installer failed (exit code {exitCode}). You can install it manually from {ReleasesUrl}.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            logger.LogInformation("HidHide installation: the elevation prompt was declined.");
            return new(HidHideInstallResult.Cancelled, "The HidHide installation was cancelled at the administrator prompt.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(HidHideInstallResult.Cancelled, "The HidHide installation was cancelled.");
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidDataException
                                              or Win32Exception or TaskCanceledException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "HidHide installation failed.");
            return new(HidHideInstallResult.Failed,
                $"HidHide could not be installed: {exception.Message} You can install it manually from {ReleasesUrl}.");
        }
        finally
        {
            TryDelete(directory);
        }
    }

    /// <summary>
    /// Maps the installer's exit code. 3010 and 1641 are the Windows
    /// installer's "restart required" codes; 1602 and 1223 are cancellation.
    /// A zero exit only counts when the driver service really appeared.
    /// </summary>
    internal static HidHideInstallResult MapInstallerExitCode(int exitCode, bool serviceInstalled) => exitCode switch
    {
        0 when serviceInstalled => HidHideInstallResult.Installed,
        3010 or 1641 => HidHideInstallResult.RebootRequired,
        1602 or ErrorCancelled => HidHideInstallResult.Cancelled,
        _ when serviceInstalled => HidHideInstallResult.Installed,
        _ => HidHideInstallResult.Failed,
    };

    private const int ErrorCancelled = 1223;

    private async Task DownloadAsync(string target, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(DownloadTimeout);
        using var client = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.None,
        })
        {
            Timeout = DownloadTimeout,
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("GameFlow-HidHideInstaller");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));

        logger.LogInformation("Downloading HidHide {Version} from {Url}.", PinnedVersion, InstallerUrl);
        using var response = await client.GetAsync(InstallerUrl, HttpCompletionOption.ResponseHeadersRead, deadline.Token)
            .ConfigureAwait(false);
        _ = response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri is { } final && final.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidDataException("The download was redirected away from HTTPS.");
        }

        var total = response.Content.Headers.ContentLength;
        if (total > MaxDownloadBytes)
        {
            throw new InvalidDataException("The HidHide installer is larger than expected.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
        await using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var buffer = new byte[81920];
        long readSoFar = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) > 0)
        {
            await file.WriteAsync(buffer.AsMemory(0, read), deadline.Token).ConfigureAwait(false);
            readSoFar += read;
            if (readSoFar > MaxDownloadBytes)
            {
                throw new InvalidDataException("The HidHide installer is larger than expected.");
            }

            if (total is > 0)
            {
                progress?.Report(Math.Clamp((double)readSoFar / total.Value, 0, 1));
            }
        }

        if (total is { } expected && expected != readSoFar)
        {
            throw new InvalidDataException("The HidHide download is incomplete.");
        }

        progress?.Report(1);
    }

    /// <summary>
    /// Puts GameFlow.exe on HidHide's allow list with HidHideCLI, so hiding a
    /// controller from games does not hide it from GameFlow too. Returns false
    /// when the CLI is not found or fails; the user can still do it by hand.
    /// </summary>
    private async Task<bool> AllowListGameFlowAsync(CancellationToken cancellationToken)
    {
        var exe = Environment.ProcessPath;
        var cli = FindHidHideCli();
        if (exe is null || cli is null)
        {
            logger.LogInformation("HidHide allow-list skipped: CLI {Cli}, process {Exe}.", cli ?? "not found", exe ?? "unknown");
            return false;
        }

        try
        {
            var exitCode = await RunElevatedAsync(cli, $"--app-reg \"{exe}\"", cancellationToken).ConfigureAwait(false);
            logger.LogInformation("HidHideCLI --app-reg exited with {ExitCode}.", exitCode);
            return exitCode == 0;
        }
        catch (Win32Exception exception)
        {
            logger.LogWarning(exception, "HidHideCLI could not be started.");
            return false;
        }
    }

    private static string? FindHidHideCli()
    {
        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetEnvironmentVariable("ProgramW6432"),
                 })
        {
            if (string.IsNullOrEmpty(root))
            {
                continue;
            }

            var path = Path.Combine(root, "Nefarius Software Solutions", "HidHide", "x64", "HidHideCLI.exe");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Starts a program through the shell with the "runas" verb, which asks
    /// for administrator rights unless GameFlow already has them (HIDMaestro
    /// needs them anyway), and waits for it to exit.
    /// </summary>
    private static async Task<int> RunElevatedAsync(string fileName, string? arguments, CancellationToken cancellationToken)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments ?? string.Empty,
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(fileName) ?? string.Empty,
        }) ?? throw new IOException($"{Path.GetFileName(fileName)} did not start.");
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }

    private void TryDelete(string? directory)
    {
        if (directory is null)
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(exception, "Could not delete {Directory}.", directory);
        }
    }
}
