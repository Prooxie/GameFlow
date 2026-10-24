using System.Diagnostics;
using GameFlow.Infrastructure.Updates;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Serilog;

namespace GameFlow.App.Views;

/// <summary>
/// Choice enum returned by <see cref="UpdateAvailableDialog"/>.
/// </summary>
public enum UpdateDialogChoice
{
    /// <summary>User closed the dialog without choosing — treat as skip-once.</summary>
    Closed,

    /// <summary>"Skip this update" — coordinator writes <c>SkippedUpdateVersion</c>.</summary>
    SkipThisUpdate,

    /// <summary>"Don't ask again" — coordinator clears <c>CheckForUpdatesOnStartup</c>.</summary>
    DontAskAgain,

    /// <summary>"Download &amp; install" — coordinator runs the installer.</summary>
    DownloadAndInstall,
}

/// <summary>
/// Dialog that surfaces an available update and lets the user choose
/// among the three actions specified in the roadmap: skip this update,
/// don't ask again, download &amp; install.
///
/// <para>
/// Code-behind only — no view-model. The hosting coordinator passes
/// the <see cref="UpdateInfo"/> via <see cref="SetUpdate"/> after
/// construction and reads back <see cref="Choice"/> after the dialog
/// closes.
/// </para>
/// </summary>
public partial class UpdateAvailableDialog : Window
{
    private UpdateInfo? update;
    private CancellationTokenSource? downloadCancellation;
    private bool downloading;
    public Func<IProgress<double>, CancellationToken, Task<bool>>? DownloadAsync { private get; set; }

    /// <summary>
    /// The user's final choice. Default is <see cref="UpdateDialogChoice.Closed"/>
    /// for the case where the user dismisses the window via the title-bar
    /// close button.
    /// </summary>
    public UpdateDialogChoice Choice { get; private set; } = UpdateDialogChoice.Closed;

    /// <summary>
    /// Constructs the dialog.
    /// </summary>
    public UpdateAvailableDialog()
    {
        InitializeComponent();
        Opened += (_, _) => WindowSizing.Fit(this);
        Closed += (_, _) => downloadCancellation?.Cancel();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Populates the dialog labels and stashes the update so the
    /// install button has something to act on. Call once before
    /// <see cref="Window.ShowDialog"/>.
    /// </summary>
    /// <param name="update">The update to surface.</param>
    /// <param name="currentVersion">The version of the running build.</param>
    public void SetUpdate(UpdateInfo update, Version currentVersion)
    {
        this.update = update ?? throw new ArgumentNullException(nameof(update));
        if (update.DownloadAssetUrl is null || update.ExpectedSha256 is null)
        {
            this.FindControl<Button>("InstallButton")!.IsEnabled = false;
            this.FindControl<Button>("InstallButton")!.Content = UiTextResources.Get("UiDownloadManually");
            this.FindControl<TextBlock>("DownloadHint")!.Text = update.DownloadAssetUrl is null
                ? UiTextResources.Get("UiNoPlatformArchive")
                : UiTextResources.Get("UiNoDownloadDigest");
        }

        var versionLine = this.FindControl<TextBlock>("VersionLine");
        if (versionLine is not null)
        {
            versionLine.Text = UiTextResources.Format("UiDownloadVersion", currentVersion, update.TagName);
        }

        var releaseName = this.FindControl<TextBlock>("ReleaseName");
        if (releaseName is not null)
        {
            releaseName.Text = update.ReleaseName;
        }

        var releaseNotesLinkText = this.FindControl<TextBlock>("ReleaseNotesLinkText");
        if (releaseNotesLinkText is not null)
        {
            releaseNotesLinkText.Text = update.ReleaseNotesUrl.ToString();
        }
    }

    /// <summary>Reports download progress to the visible progress bar.</summary>
    private void ReportProgress(double fraction)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!downloading) { return; }
            var area = this.FindControl<StackPanel>("ProgressArea");
            var bar = this.FindControl<ProgressBar>("DownloadProgress");
            var line = this.FindControl<TextBlock>("ProgressLine");

            if (area is not null)
            {
                area.IsVisible = true;
            }
            if (bar is not null)
            {
                bar.Value = Math.Clamp(fraction, 0.0, 1.0);
            }
            if (line is not null)
            {
                line.Text = UiTextResources.Format("UiDownloadProgress", (int)Math.Round(fraction * 100));
            }
        });
    }

    /// <summary>
    /// Opens the release notes URL in the default browser. Failures
    /// are logged at Debug — the dialog stays open.
    /// </summary>
    private void OnReleaseNotesClick(object? sender, RoutedEventArgs e)
    {
        if (update is null)
        {
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = update.ReleaseNotesUrl.ToString(),
                UseShellExecute = true,
            });
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Could not open release notes URL {Url}.", update.ReleaseNotesUrl);
        }
    }

    private void OnSkipClick(object? sender, RoutedEventArgs e)
    {
        Choice = UpdateDialogChoice.SkipThisUpdate;
        Close();
    }

    private void OnDontAskClick(object? sender, RoutedEventArgs e)
    {
        Choice = UpdateDialogChoice.DontAskAgain;
        Close();
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (Choice == UpdateDialogChoice.DownloadAndInstall) { Close(); return; }
        if (DownloadAsync is null || downloading) { return; }
        using var cancellation = new CancellationTokenSource();
        downloadCancellation = cancellation;
        downloading = true;
        var installButton = this.FindControl<Button>("InstallButton")!;
        installButton.IsEnabled = false;
        installButton.Content = UiTextResources.Get("UiDownloading");
        this.FindControl<Button>("SkipButton")!.IsEnabled = false;
        this.FindControl<Button>("DontAskButton")!.IsEnabled = false;
        this.FindControl<Button>("CancelDownloadButton")!.IsVisible = true;
        this.FindControl<StackPanel>("ProgressArea")!.IsVisible = true;
        this.FindControl<TextBlock>("ProgressLine")!.Text = UiTextResources.Get("UiStartingDownload");
        bool success = false;
        try
        {
            success = await DownloadAsync(new Progress<double>(ReportProgress), cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception) { Log.Warning(exception, "Update download failed."); }
        finally
        {
            downloading = false;
            downloadCancellation = null;
            this.FindControl<Button>("CancelDownloadButton")!.IsVisible = false;
            this.FindControl<Button>("SkipButton")!.IsEnabled = true;
            this.FindControl<Button>("DontAskButton")!.IsEnabled = true;
        }
        if (success)
        {
            Choice = UpdateDialogChoice.DownloadAndInstall;
            installButton.Content = "Done";
            installButton.IsEnabled = true;
            this.FindControl<Button>("SkipButton")!.IsVisible = false;
            this.FindControl<Button>("DontAskButton")!.IsVisible = false;
            this.FindControl<TextBlock>("ProgressLine")!.Text = UiTextResources.Get("UiDownloaded");
        }
        else
        {
            installButton.IsEnabled = true;
            installButton.Content = UiTextResources.Get("UiRetryDownload");
            this.FindControl<TextBlock>("ProgressLine")!.Text = cancellation.IsCancellationRequested
                ? UiTextResources.Get("UiDownloadCancelled")
                : UiTextResources.Get("UiDownloadFailed");
        }
    }

    private void OnCancelDownloadClick(object? sender, RoutedEventArgs e) => downloadCancellation?.Cancel();
}
