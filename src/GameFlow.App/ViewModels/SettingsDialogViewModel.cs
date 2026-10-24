using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Localization;
using GameFlow.Infrastructure.Profiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace GameFlow.App.ViewModels;

/// <summary>
/// View-model for the Options / Settings dialog (Step 5 of the roadmap).
///
/// <para>
/// Exposes every user-editable setting persisted on
/// <see cref="AppSettings"/> as an observable property, plus the
/// <see cref="ApplyAsync"/> / <see cref="RestoreDefaults"/> /
/// <see cref="Reload"/> actions that write back to
/// <see cref="IUserSettingsService"/> and refresh the dialog from the
/// current persisted state.
/// </para>
///
/// <para>
/// Settings outside the dialog's purview (currently:
/// <see cref="AppSettings.ActiveProfileId"/>,
/// <see cref="AppSettings.SelectedCulture"/>, and
/// <see cref="AppSettings.SkippedUpdateVersion"/>) are preserved through
/// the apply round-trip so toggling something here doesn't accidentally
/// reset the active profile.
/// </para>
/// </summary>
public sealed partial class SettingsDialogViewModel : ObservableObject, IDisposable
{
    private readonly IUserSettingsService userSettings;
    private readonly ILocalizationService localization;
    private readonly ILogger<SettingsDialogViewModel> logger;

    /// <summary>
    /// Optional back-reference to the <see cref="ShellViewModel"/> so the
    /// Settings dialog can reuse the shell's existing Language + Theme
    /// bindings (no duplication of apply/persist logic). Set by
    /// <c>OpenSettingsAsync</c> just before <c>ShowDialog</c>.
    /// </summary>
    public ShellViewModel? Shell { get; set; }

    /// <summary>Log levels offered by the dialog dropdown, in order.</summary>
    public IReadOnlyList<LogLevel> AvailableLogLevels { get; } =
    [
        LogLevel.Trace,
        LogLevel.Debug,
        LogLevel.Information,
        LogLevel.Warning,
        LogLevel.Error,
        LogLevel.Critical,
    ];

    [ObservableProperty]
    private LogLevel selectedLogLevel = LogLevel.Information;

    [ObservableProperty]
    private bool rememberWindowSize = true;

    [ObservableProperty]
    private double windowWidth = 1280;

    [ObservableProperty]
    private double windowHeight = 800;

    [ObservableProperty]
    private string dashboardRefreshHzText = string.Empty;

    [ObservableProperty]
    private string pollingRateHzText = string.Empty;

    [ObservableProperty]
    private string profilesDirectoryOverride = string.Empty;

    [ObservableProperty]
    private string logsDirectoryOverride = string.Empty;

    [ObservableProperty]
    private bool checkForUpdatesOnStartup = true;

    [ObservableProperty]
    private bool checkRequirementsOnStartup = true;

    /// <summary>Status line shown at the bottom of the dialog after Apply.</summary>
    [ObservableProperty]
    private string statusMessage = string.Empty;

    /// <summary>
    /// DSU / Cemuhook motion server controls. The shared singleton, so the
    /// live status line here is the same one the runtime is updating — and
    /// so the toggle is a single global switch. The server reads whichever
    /// slots are enabled; it is not a per-controller setting, which is why
    /// it lives in Settings rather than on a virtual controller.
    ///
    /// <para>
    /// This panel owns its own persistence (it writes on change rather
    /// than on Apply), so it is deliberately NOT part of
    /// <see cref="ApplyAsync"/> / <see cref="RestoreDefaults"/>.
    /// </para>
    /// </summary>
    public MotionServerPanelViewModel MotionServer { get; }

    /// <summary>
    /// The web server's network side, shown on the "Network &amp; phones"
    /// tab: the phone-sharing switch, the private-link reset and the
    /// firewall reachability warning. Only those members are bound here —
    /// the OBS overlay URL moved onto each controller panel on the
    /// dashboard, and phones are added one at a time from Devices, each
    /// with its own link, so this dialog no longer shows a URL of any kind.
    ///
    /// <para>
    /// Like <see cref="MotionServer"/> it is outside
    /// <see cref="ApplyAsync"/> / <see cref="RestoreDefaults"/>: sharing
    /// saves the moment it is toggled, so there is nothing to apply.
    /// </para>
    /// </summary>
    public OverlayPanelViewModel Overlay { get; }

    /// <summary>
    /// The per-app profile section. Persists on change like
    /// <see cref="MotionServer"/>, so it too sits outside
    /// <see cref="ApplyAsync"/> / <see cref="RestoreDefaults"/>.
    /// </summary>
    public AppProfilePanelViewModel AppProfiles { get; }

    // ─── Tabs ─────────────────────────────────────────────────────────────────

    // Positions of the dialog's TabItems, in XAML order: General, Input &
    // performance, Profiles, Network & phones, Advanced. A reorder in
    // SettingsDialog.axaml has to be mirrored here.
    private const int GeneralTabIndex = 0;
    private const int InputPerformanceTabIndex = 1;
    private const int TabCount = 5;

    /// <summary>
    /// The tab last shown, kept for the life of the process.
    /// </summary>
    /// <remarks>
    /// Static because the view-model is transient — a fresh one per
    /// opening — and the point is to survive that: someone adjusting the
    /// motion server, closing Options to try it, and reopening to tweak
    /// the port should land back on that page, not on General. Not
    /// persisted to settings; a new session starts on General, which is
    /// the right first page for someone who has not been here in a while.
    /// </remarks>
    private static int lastSelectedTabIndex = GeneralTabIndex;

    /// <summary>
    /// Bound two-way to the dialog's TabControl.
    /// </summary>
    /// <remarks>
    /// Negative values are ignored rather than stored: a TabControl
    /// reports -1 while it has no selection (as its items are torn down
    /// when the window closes), and remembering that would reopen the
    /// dialog on a blank page.
    /// </remarks>
    public int SelectedTabIndex
    {
        get => lastSelectedTabIndex;
        set
        {
            if (value < 0 || value >= TabCount || value == lastSelectedTabIndex)
            {
                return;
            }

            lastSelectedTabIndex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Tab headers. English fallbacks until the catalogs carry the keys.</summary>
    public string GeneralTabHeader => UiTextResources.Get("UiGeneral");
    public string InputPerformanceTabHeader => UiTextResources.Get("UiInputPerformance");
    public string ProfilesTabHeader => localization["ProfilesTab"];
    public string NetworkTabHeader => UiTextResources.Get("UiNetworkPhones");
    public string AdvancedTabHeader => UiTextResources.Get("UiAdvanced");

    /// <summary>
    /// Looks up <paramref name="key"/>, falling back to English when no
    /// catalog has it. The localizer answers a missing key with the key
    /// itself, and a tab labelled "SettingsDialogNetworkTab" is worse than
    /// an untranslated one.
    /// </summary>
    private string Loc(string key, string fallback)
    {
        var value = localization[key];
        return string.Equals(value, key, StringComparison.Ordinal) ? fallback : value;
    }

    // ─── Localised labels ─────────────────────────────────────────────────────

    /// <summary>Window title.</summary>
    public string TitleLabel => localization["SettingsDialogTitle"];
    public string DiagnosticsHeaderLabel => localization["SettingsDialogDiagnosticsHeader"];
    public string LogLevelLabel => localization["SettingsDialogLogLevelLabel"];
    public string LogLevelHintLabel => localization["SettingsDialogLogLevelHint"];
    public string WindowHeaderLabel => localization["SettingsDialogWindowHeader"];
    public string RememberWindowSizeLabel => localization["SettingsDialogRememberWindowSizeLabel"];
    public string WindowWidthLabel => localization["SettingsDialogWindowWidthLabel"];
    public string WindowHeightLabel => localization["SettingsDialogWindowHeightLabel"];
    public string PollingHeaderLabel => localization["SettingsDialogPollingHeader"];
    public string DashboardRefreshLabel => localization["SettingsDialogDashboardRefreshLabel"];
    public string PollingRateLabel => localization["SettingsDialogPollingRateLabel"];
    public string PollingHintLabel => localization["SettingsDialogPollingHint"];
    public string PathsHeaderLabel => localization["SettingsDialogPathsHeader"];
    public string ProfilesDirectoryLabel => localization["SettingsDialogProfilesDirectoryLabel"];
    public string LogsDirectoryLabel => localization["SettingsDialogLogsDirectoryLabel"];
    public string LogsPathHintLabel => localization["SettingsDialogLogsPathHint"];
    public string BrowseButtonLabel => localization["SettingsDialogBrowseButton"];
    public string StartupHeaderLabel => localization["SettingsDialogStartupHeader"];
    public string CheckUpdatesLabel => localization["SettingsDialogCheckUpdatesLabel"];
    public string CheckRequirementsLabel => localization["SettingsDialogCheckRequirementsLabel"];
    public string ApplyButtonLabel => localization["SettingsDialogApplyButton"];
    public string CancelButtonLabel => localization["SettingsDialogCancelButton"];
    public string RestoreDefaultsButtonLabel => localization["SettingsDialogRestoreDefaultsButton"];

    /// <summary>Path the dialog should show as the in-effect profiles dir.</summary>
    public string CurrentProfilesPath => AppPaths.ProfilesDirectory;

    /// <summary>Path the dialog should show as the in-effect logs dir.</summary>
    public string CurrentLogsPath => AppPaths.LogsDirectory;

    /// <summary>
    /// Constructs the view-model. Pre-populates every observable
    /// property from <see cref="IUserSettingsService.Current"/>.
    /// </summary>
    private readonly IServiceProvider services;

    /// <summary>
    /// Builds a fresh walkthrough for the "Run setup walkthrough" button.
    /// </summary>
    /// <remarks>
    /// Resolved from the container rather than constructed here, so it
    /// gets the live device catalog and slot registry the rest of the app
    /// is using. A new instance per press: the walkthrough is a one-shot
    /// state machine that starts at step one and disposes its catalog
    /// subscription when its window closes.
    /// </remarks>
    public SetupWalkthroughViewModel CreateWalkthrough() =>
        (SetupWalkthroughViewModel)services.GetService(typeof(SetupWalkthroughViewModel))!;

    public SettingsDialogViewModel(
        IUserSettingsService userSettings,
        ILocalizationService localization,
        MotionServerPanelViewModel motionServer,
        OverlayPanelViewModel overlay,
        AppProfilePanelViewModel appProfiles,
        IServiceProvider services,
        ILogger<SettingsDialogViewModel> logger)
    {
        this.userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
        this.localization = localization ?? throw new ArgumentNullException(nameof(localization));
        MotionServer = motionServer ?? throw new ArgumentNullException(nameof(motionServer));
        Overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        AppProfiles = appProfiles ?? throw new ArgumentNullException(nameof(appProfiles));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Subscribe to culture changes so the dialog's labels follow
        // the user's language pick while the dialog is open. Every
        // localized label here is a computed-on-demand property
        // (=> localization["..."]), so we re-fire OnPropertyChanged
        // for each one when the culture changes — that's the signal
        // Avalonia's bindings need to re-call the getter and pick up
        // the new culture's translation.
        this.localization.CultureChanged += OnCultureChanged;

        // Diagnostic: capture the ambient UI culture and a sample
        // resolved label at construction time. If the user reports the
        // dialog is "stuck in the startup language", this single line
        // tells us which half of the system to blame:
        //   • culture == new + label == translated  → binding/render bug
        //   • culture == new + label == English      → PO lookup / fallback
        //   • culture == startup                     → SetCulture isn't
        //     reaching this thread's ambient culture before construction
        logger.LogInformation(
            "SettingsDialog VM constructed. CurrentUICulture={Culture}, ILocalizationService.CurrentCulture={SvcCulture}, sample TitleLabel='{Title}'.",
            System.Globalization.CultureInfo.CurrentUICulture.Name,
            localization.CurrentCulture,
            localization["SettingsDialogTitle"]);

        Reload();
    }

    // One list of label names, in RefreshLocalisedLabels: this used to
    // carry its own copy, and two copies of a list that must stay
    // identical is how a new label ends up refreshing on one path only.
    private void OnCultureChanged(object? sender, EventArgs e) => RefreshLocalisedLabels();

    /// <summary>
    /// Unsubscribes from <see cref="ILocalizationService.CultureChanged"/>.
    /// Called by the dialog's <c>Closed</c> handler so a long-lived
    /// localization service doesn't hold this short-lived view-model
    /// alive via the event handler.
    /// </summary>
    public void Dispose()
    {
        localization.CultureChanged -= OnCultureChanged;
    }

    /// <summary>
    /// Reloads every observable property from the persisted settings
    /// snapshot. Called from the constructor and the Cancel button.
    /// </summary>
    public void Reload()
    {
        var s = userSettings.Current;
        SelectedLogLevel = s.LogLevel;
        RememberWindowSize = s.RememberWindowSize;
        WindowWidth = s.WindowWidth;
        WindowHeight = s.WindowHeight;
        DashboardRefreshHzText = s.DashboardRefreshHz?.ToString() ?? string.Empty;
        PollingRateHzText = s.PollingRateHz?.ToString() ?? string.Empty;
        ProfilesDirectoryOverride = s.ProfilesDirectoryOverride ?? string.Empty;
        LogsDirectoryOverride = s.LogsDirectoryOverride ?? string.Empty;
        CheckForUpdatesOnStartup = s.CheckForUpdatesOnStartup;
        CheckRequirementsOnStartup = s.CheckRequirementsOnStartup;
        StatusMessage = string.Empty;
    }

    /// <summary>
    /// Resets every dialog field to its hard-coded default. Does NOT
    /// persist; the user still has to click Apply.
    /// </summary>
    [RelayCommand]
    public void RestoreDefaults()
    {
        var d = new AppSettings();
        SelectedLogLevel = d.LogLevel;
        RememberWindowSize = d.RememberWindowSize;
        WindowWidth = d.WindowWidth;
        WindowHeight = d.WindowHeight;
        DashboardRefreshHzText = string.Empty;
        PollingRateHzText = string.Empty;
        ProfilesDirectoryOverride = string.Empty;
        LogsDirectoryOverride = string.Empty;
        CheckForUpdatesOnStartup = d.CheckForUpdatesOnStartup;
        CheckRequirementsOnStartup = d.CheckRequirementsOnStartup;
        StatusMessage = localization["SettingsDialogRestoredStatus"];
    }

    /// <summary>
    /// Folds the dialog state into <see cref="AppSettings"/> and writes
    /// it back through <see cref="IUserSettingsService.UpdateAsync"/>.
    /// Returns <see langword="true"/> on success.
    /// </summary>
    [RelayCommand]
    public async Task<bool> ApplyAsync()
    {
        // The two rate boxes are the only fields that can be rejected, and
        // both live on the Input & performance tab. The error lands in the
        // footer, which every tab shares — so without switching to that tab,
        // someone pressing Apply from General reads "enter a rate between 30
        // and 1000" with no rate box anywhere in sight. Validated apart from
        // the save below so that only THIS failure moves the user.
        int? dashboardRefreshHz;
        int? pollingRateHz;
        try
        {
            dashboardRefreshHz = ParseOptionalInt(DashboardRefreshHzText, min: 30, max: 1000);
            pollingRateHz = ParseOptionalInt(PollingRateHzText, min: 30, max: 1000);
        }
        catch (FormatException exception)
        {
            logger.LogInformation("Settings dialog Apply rejected a polling rate: {Message}", exception.Message);
            SelectedTabIndex = InputPerformanceTabIndex;
            StatusMessage = string.Format(localization["SettingsDialogApplyFailedStatus"], exception.Message);
            return false;
        }

        try
        {
            // Read the dialog on the UI thread, before the change runs under
            // the settings lock. Only the fields this dialog owns change.
            var logLevel = SelectedLogLevel;
            var rememberWindowSize = RememberWindowSize;
            var windowWidth = ClampWindowDimension(WindowWidth, defaultValue: 1280);
            var windowHeight = ClampWindowDimension(WindowHeight, defaultValue: 800);
            var profilesDirectory = string.IsNullOrWhiteSpace(ProfilesDirectoryOverride) ? null : ProfilesDirectoryOverride.Trim();
            var logsDirectory = string.IsNullOrWhiteSpace(LogsDirectoryOverride) ? null : LogsDirectoryOverride.Trim();
            var checkForUpdates = CheckForUpdatesOnStartup;
            var checkRequirements = CheckRequirementsOnStartup;

            // This command is entered on Avalonia's UI thread. Preserve its
            // synchronization context for both success and failure notifications.
            await userSettings.UpdateAsync(settings => settings with
            {
                LogLevel = logLevel,
                RememberWindowSize = rememberWindowSize,
                WindowWidth = windowWidth,
                WindowHeight = windowHeight,
                DashboardRefreshHz = dashboardRefreshHz,
                PollingRateHz = pollingRateHz,
                ProfilesDirectoryOverride = profilesDirectory,
                LogsDirectoryOverride = logsDirectory,
                CheckForUpdatesOnStartup = checkForUpdates,
                CheckRequirementsOnStartup = checkRequirements,
            });

            // The path-override change may have moved the in-effect
            // directories; refresh the read-only display rows so the user
            // can see the result.
            OnPropertyChanged(nameof(CurrentProfilesPath));
            OnPropertyChanged(nameof(CurrentLogsPath));

            StatusMessage = localization["SettingsDialogAppliedStatus"];
            return true;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not apply settings dialog changes.");
            StatusMessage = string.Format(localization["SettingsDialogApplyFailedStatus"], exception.Message);
            return false;
        }
    }

    /// <summary>
    /// Refreshes every localised label property — invoked from the
    /// hosting dialog when the culture changes, in case the user opens
    /// settings, switches language somewhere else, and comes back.
    /// </summary>
    public void RefreshLocalisedLabels()
    {
        OnPropertyChanged(nameof(TitleLabel));
        OnPropertyChanged(nameof(DiagnosticsHeaderLabel));
        OnPropertyChanged(nameof(LogLevelLabel));
        OnPropertyChanged(nameof(LogLevelHintLabel));
        OnPropertyChanged(nameof(WindowHeaderLabel));
        OnPropertyChanged(nameof(RememberWindowSizeLabel));
        OnPropertyChanged(nameof(WindowWidthLabel));
        OnPropertyChanged(nameof(WindowHeightLabel));
        OnPropertyChanged(nameof(PollingHeaderLabel));
        OnPropertyChanged(nameof(DashboardRefreshLabel));
        OnPropertyChanged(nameof(PollingRateLabel));
        OnPropertyChanged(nameof(PollingHintLabel));
        OnPropertyChanged(nameof(PathsHeaderLabel));
        OnPropertyChanged(nameof(ProfilesDirectoryLabel));
        OnPropertyChanged(nameof(LogsDirectoryLabel));
        OnPropertyChanged(nameof(LogsPathHintLabel));
        OnPropertyChanged(nameof(BrowseButtonLabel));
        OnPropertyChanged(nameof(StartupHeaderLabel));
        OnPropertyChanged(nameof(CheckUpdatesLabel));
        OnPropertyChanged(nameof(CheckRequirementsLabel));
        OnPropertyChanged(nameof(ApplyButtonLabel));
        OnPropertyChanged(nameof(CancelButtonLabel));
        OnPropertyChanged(nameof(RestoreDefaultsButtonLabel));
        OnPropertyChanged(nameof(GeneralTabHeader));
        OnPropertyChanged(nameof(InputPerformanceTabHeader));
        OnPropertyChanged(nameof(ProfilesTabHeader));
        OnPropertyChanged(nameof(NetworkTabHeader));
        OnPropertyChanged(nameof(AdvancedTabHeader));
    }

    /// <summary>
    /// Whitespace selects the automatic rate; invalid input keeps the dialog open.
    /// </summary>
    private static int? ParseOptionalInt(string text, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!int.TryParse(text.Trim(), out var parsed) || parsed < min || parsed > max)
        {
            throw new FormatException($"Enter a whole-number rate between {min} and {max} Hz, or leave it empty for automatic.");
        }
        return parsed;
    }

    /// <summary>
    /// Clamps a window dimension to a sane range. Negative values, zero,
    /// or anything past 8000 collapses back to <paramref name="defaultValue"/>.
    /// </summary>
    private static double ClampWindowDimension(double value, double defaultValue)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 200 || value > 8000)
        {
            return defaultValue;
        }
        return value;
    }
}
