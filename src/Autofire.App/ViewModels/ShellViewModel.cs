using System.Reflection;
using System.Text;
using System.Text.Json;
using Autofire.App.Services;
using Autofire.Core.Enums;
using Autofire.Core.Models;
using Autofire.Infrastructure.Configuration;
using Autofire.Infrastructure.Localization;
using Autofire.Infrastructure.Profiles;
using Autofire.Infrastructure.Runtime;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autofire.App.ViewModels;

/// <summary>
/// Shell-level view-model.
///
/// Performance contract:
///   Fast path  (every 33 ms tick): update controller visuals, status text,
///              diagnostics text (no longer throttled — cheap string build).
///   Medium path (~0.5 s / 15 ticks): rebuild controller inventory, runtime notes.
///   Slow path  (~3 s / 90 ticks): rebuild JSON snapshots.
///   Event path (profile or culture changed): rebuild rule summary, re-sync selections.
/// </summary>
public sealed class ShellViewModel : ViewModelBase, IDisposable
{
    private const int SlowPathEvery = 90;
    private const int MedPathEvery  = 15;
    private int refreshTick;

    private bool ruleSummaryDirty = true;
    private bool aboutTextDirty   = true;
    private bool jsonDirty        = true;

    private readonly ProfileSession profileSession;
    private readonly RuntimeSnapshotStore runtimeSnapshotStore;
    private readonly ILocalizationService localizationService;
    private readonly InputDeviceCatalog inputDeviceCatalog;
    private readonly IProfileFileDialogService profileFileDialogService;
    private readonly ILoggerFactory loggerFactory;
    private readonly ILogger<ShellViewModel> logger;
    private readonly AppRuntimeOptions runtimeOptions;
    private readonly SemaphoreSlim rulesSaveGate = new(1, 1);

    private LanguageOption? selectedLanguage;
    private AppThemeOption? selectedTheme;
    private DetectedControllerOption? selectedController;
    private ProfileOption? selectedProfileOption;
    private string providerSummary = string.Empty;
    private string? selectedControlKey;
    private bool isSwitchingProfile;
    private bool disposed;

    public static string AppVersion { get; } =
        Assembly.GetEntryAssembly()?.GetName().Version is { } v
            ? $"v{v.Major}.{v.Minor}.{v.Build}"
            : "v1.0.0";

    public static string AppFooterText { get; } =
        $"Made by Proxy Darkness  ·  {AppVersion}  ·  © 2026";

    public ShellViewModel(
        ProfileSession profileSession,
        RuntimeSnapshotStore runtimeSnapshotStore,
        ILocalizationService localizationService,
        InputDeviceCatalog inputDeviceCatalog,
        IProfileFileDialogService profileFileDialogService,
        IOptions<AppRuntimeOptions> runtimeOptions,
        ILoggerFactory loggerFactory,
        ILogger<ShellViewModel> logger)
    {
        this.profileSession = profileSession;
        this.runtimeSnapshotStore = runtimeSnapshotStore;
        this.localizationService = localizationService;
        this.inputDeviceCatalog = inputDeviceCatalog;
        this.profileFileDialogService = profileFileDialogService;
        this.runtimeOptions = runtimeOptions.Value;
        this.loggerFactory = loggerFactory;
        this.logger = logger;

        SaveProfileCommand               = new AsyncRelayCommand(SaveProfileAsync);
        ResetProfileCommand              = new AsyncRelayCommand(ResetProfileAsync);
        ApplyDashboardPreferencesCommand = new AsyncRelayCommand(ApplyDashboardPreferencesAsync);
        CreateProfileCommand             = new AsyncRelayCommand(CreateProfileAsync);
        DuplicateProfileCommand          = new AsyncRelayCommand(DuplicateProfileAsync);
        ImportProfileCommand             = new AsyncRelayCommand(ImportProfileAsync);
        ExportProfileCommand             = new AsyncRelayCommand(ExportProfileAsync);
        RenameProfileCommand             = new AsyncRelayCommand(RenameProfileAsync);
        OpenControlEditorCommand         = new RelayCommand<string>(OpenControlEditor);

        SupportedLanguages     = localizationService.SupportedLanguages;
        ThemeOptions           = CreateThemeOptions();
        InputProviderOptions   = CreateInputProviderOptions();
        OutputProviderOptions  = CreateOutputProviderOptions();
        ControllerStyleOptions = CreateControllerStyleOptions();
        MappingEditor          = new MappingEditorViewModel(loggerFactory.CreateLogger<MappingEditorViewModel>());
        MappingEditor.RulesChanged += OnMappingRulesChanged;

        selectedLanguage = SupportedLanguages
            .FirstOrDefault(l => l.Code == localizationService.CurrentCulture)
            ?? SupportedLanguages.FirstOrDefault();

        selectedTheme = ThemeOptions.FirstOrDefault(t => t.Kind == AppThemeKind.CyberBlue)
            ?? ThemeOptions.FirstOrDefault();

        PhysicalController = new ControllerVisualStateViewModel(OnControllerElementSelected);
        VirtualController  = new ControllerVisualStateViewModel(OnControllerElementSelected);

        providerSummary = string.Join(
            Environment.NewLine,
            ProviderCatalog.KnownProviders.Select(p =>
                $"- {p.DisplayName}: {(p.IsImplemented ? "available" : "planned")} — {p.Notes}"));

        localizationService.CultureChanged += OnCultureChanged;
        profileSession.Changed             += OnProfileChanged;
    }

    // ─── Commands ─────────────────────────────────────────────────────────────

    public IAsyncRelayCommand SaveProfileCommand               { get; }
    public IAsyncRelayCommand ResetProfileCommand              { get; }
    public IAsyncRelayCommand ApplyDashboardPreferencesCommand { get; }
    public IAsyncRelayCommand CreateProfileCommand             { get; }
    public IAsyncRelayCommand DuplicateProfileCommand          { get; }
    public IAsyncRelayCommand ImportProfileCommand             { get; }
    public IAsyncRelayCommand ExportProfileCommand             { get; }
    public IAsyncRelayCommand RenameProfileCommand             { get; }
    public IRelayCommand<string> OpenControlEditorCommand      { get; }

    public event EventHandler<ControlMappingRequestedEventArgs>? ControlMappingRequested;

    // ─── Collections ──────────────────────────────────────────────────────────

    public IReadOnlyList<LanguageOption> SupportedLanguages    { get; }
    public IReadOnlyList<AppThemeOption> ThemeOptions          { get; }
    public IReadOnlyList<InputProviderOption> InputProviderOptions  { get; }
    public IReadOnlyList<OutputProviderOption> OutputProviderOptions { get; }
    public IReadOnlyList<ControllerStyleOption> ControllerStyleOptions { get; }

    public IReadOnlyList<DetectedControllerOption> AvailableControllers
    {
        get; private set => SetProperty(ref field, value);
    } = [];

    public IReadOnlyList<ProfileOption> AvailableProfiles
    {
        get; private set => SetProperty(ref field, value);
    } = [];

    public IReadOnlyList<ControlConfigurationCardViewModel> ControlConfigurationCards
    {
        get; private set => SetProperty(ref field, value);
    } = [];

    public MappingEditorViewModel MappingEditor { get; }
    public ControllerVisualStateViewModel PhysicalController { get; }
    public ControllerVisualStateViewModel VirtualController  { get; }

    // ─── Observable properties ────────────────────────────────────────────────

    public string WindowTitle
    {
        get; private set => SetProperty(ref field, value);
    } = "Autofire Next";

    public string StatusText
    {
        get; private set => SetProperty(ref field, value);
    } = string.Empty;

    public string PhysicalStateJson
    {
        get; private set => SetProperty(ref field, value);
    } = "{}";

    public string VirtualStateJson
    {
        get; private set => SetProperty(ref field, value);
    } = "{}";

    public string ProfileJson
    {
        get; set => SetProperty(ref field, value);
    } = "{}";

    public string DiagnosticsText
    {
        get; private set => SetProperty(ref field, value);
    } = string.Empty;

    public string AboutText
    {
        get; private set => SetProperty(ref field, value);
    } = string.Empty;

    public string RuleSummary
    {
        get; private set => SetProperty(ref field, value);
    } = string.Empty;

    public string ProviderSummary
    {
        get => providerSummary;
        private set => SetProperty(ref providerSummary, value);
    }

    public string SelectedControlTitle
    {
        get; private set => SetProperty(ref field, value);
    } = string.Empty;

    public string SelectedControlValue
    {
        get; private set => SetProperty(ref field, value);
    } = string.Empty;

    public string SelectedControlRules
    {
        get; private set => SetProperty(ref field, value);
    } = string.Empty;

    public string SelectedControlHint
    {
        get; private set => SetProperty(ref field, value);
    } = string.Empty;

    public string RuntimeNotesText
    {
        get; private set => SetProperty(ref field, value);
    } = string.Empty;

    public string ControllerInventoryText
    {
        get; private set => SetProperty(ref field, value);
    } = string.Empty;

    public string ProviderStatusText
    {
        get; private set => SetProperty(ref field, value);
    } = string.Empty;

    public string ProfileName
    {
        get; set => SetProperty(ref field, value);
    } = string.Empty;

    // ─── Localised label properties ───────────────────────────────────────────

    public string DashboardTabLabel              => localizationService["DashboardTab"];
    public string ProfilesTabLabel               => localizationService["ProfilesTab"];
    public string DiagnosticsTabLabel            => localizationService["DiagnosticsTab"];
    public string LanguageLabel                  => localizationService["LanguageLabel"];
    public string ThemeLabel                     => "Theme";
    public string SaveProfileButtonLabel         => localizationService["SaveProfileButton"];
    public string ResetProfileButtonLabel        => localizationService["ResetProfileButton"];
    public string PhysicalInputLabel             => localizationService["PhysicalInputLabel"];
    public string VirtualOutputLabel             => localizationService["VirtualOutputLabel"];
    public string RuleSummaryLabel               => localizationService["RuleSummaryLabel"];
    public string ProfileEditorLabel             => localizationService["ProfileEditorLabel"];
    public string DiagnosticsLabel               => localizationService["DiagnosticsLabel"];
    public string DashboardSettingsLabel         => localizationService["DashboardSettingsLabel"];
    public string DashboardSubtitle              => localizationService["DashboardSubtitle"];
    public string InputProviderLabel             => localizationService["InputProviderLabel"];
    public string OutputProviderLabel            => localizationService["OutputProviderLabel"];
    public string ControllerLabel                => localizationService["ControllerLabel"];
    public string ControllerInventoryLabel       => localizationService["ControllerInventoryLabel"];
    public string ProviderStatusLabel            => localizationService["ProviderStatusLabel"];
    public string PollingRateLabel               => localizationService["PollingRateLabel"];
    public string PhysicalStyleLabel             => localizationService["PhysicalStyleLabel"];
    public string VirtualStyleLabel              => localizationService["VirtualStyleLabel"];
    public string ApplyDashboardSettingsButtonLabel => localizationService["ApplyDashboardSettingsButtonLabel"];
    public string SelectedControlLabel           => localizationService["SelectedControlLabel"];
    public string RuntimeNotesLabel              => localizationService["RuntimeNotesLabel"];
    public string RawPhysicalLabel               => localizationService["RawPhysicalLabel"];
    public string RawVirtualLabel                => localizationService["RawVirtualLabel"];
    public string ProfileComboLabel              => localizationService["ProfileComboLabel"];
    public string ProfileToolbarSubtitle         => localizationService["ProfileToolbarSubtitle"];
    public string ProfileNameLabel               => localizationService["ProfileNameLabel"];
    public string ProfileNameWatermark           => localizationService["ProfileNameWatermark"];
    public string RenameProfileButtonLabel       => localizationService["RenameProfileButton"];
    public string CreateProfileButtonLabel       => localizationService["CreateProfileButton"];
    public string DuplicateProfileButtonLabel    => localizationService["DuplicateProfileButton"];
    public string ImportProfileButtonLabel       => localizationService["ImportProfileButton"];
    public string ExportProfileButtonLabel       => localizationService["ExportProfileButton"];
    public string MappingOverviewLabel           => localizationService["MappingOverviewLabel"];
    public string MappingOverviewSubtitle        => localizationService["MappingOverviewSubtitle"];
    public string OpenControlEditorButtonLabel   => localizationService["OpenControlEditorButton"];
    public string NoMappingCardsText             => localizationService["NoMappingCardsText"];

    public string SelectedInputProviderSummary =>
        SelectedInputProvider?.Label ?? "—";

    public string SelectedOutputProviderSummary =>
        SelectedOutputProvider?.Label ?? "—";

    public string SelectedControllerSummary =>
        SelectedController is null || string.IsNullOrWhiteSpace(SelectedController.Id)
            ? localizationService["AutomaticControllerSelection"]
            : SelectedController.Label;

    public string SelectedPollingRateText => $"{SelectedPollingRateHz:0} Hz";
    public string SelectedProfileSummary  => SelectedProfileOption?.Description ?? string.Empty;

    public bool HasControlConfigurationCards   => ControlConfigurationCards.Count > 0;
    public bool HasNoControlConfigurationCards => !HasControlConfigurationCards;

    // ─── Mutable selection properties ─────────────────────────────────────────

    public LanguageOption? SelectedLanguage
    {
        get => selectedLanguage;
        set
        {
            if (!SetProperty(ref selectedLanguage, value) || value is null)
            {
                return;
            }

            localizationService.SetCulture(value.Code);
            _ = profileSession.SetCultureAsync(value.Code);
            RefreshLocalizedText();
        }
    }

    public AppThemeOption? SelectedTheme
    {
        get => selectedTheme;
        set
        {
            if (!SetProperty(ref selectedTheme, value) || value is null)
            {
                return;
            }

            AppThemeService.Apply(value.Kind);
        }
    }

    public InputProviderOption? SelectedInputProvider
    {
        get; set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedInputProviderSummary));
        }
    }

    public OutputProviderOption? SelectedOutputProvider
    {
        get; set
        {
            if (!SetProperty(ref field, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedOutputProviderSummary));
        }
    }

    public ControllerStyleOption? SelectedPhysicalStyle
    {
        get; set => SetProperty(ref field, value);
    }

    public ControllerStyleOption? SelectedVirtualStyle
    {
        get; set => SetProperty(ref field, value);
    }

    public DetectedControllerOption? SelectedController
    {
        get => selectedController;
        set
        {
            if (!SetProperty(ref selectedController, value))
            {
                return;
            }

            inputDeviceCatalog.SetSelectedDevice(
                string.IsNullOrWhiteSpace(value?.Id) ? null : value!.Id);
            OnPropertyChanged(nameof(SelectedControllerSummary));
        }
    }

    public ProfileOption? SelectedProfileOption
    {
        get => selectedProfileOption;
        set
        {
            if (!SetProperty(ref selectedProfileOption, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedProfileSummary));

            if (value is null || isSwitchingProfile ||
                string.Equals(value.Id, profileSession.CurrentProfile.Id, StringComparison.Ordinal))
            {
                return;
            }

            _ = SwitchToProfileAsync(value.Id);
        }
    }

    public double SelectedPollingRateHz
    {
        get; set
        {
            var normalized = Math.Clamp(Math.Round(value), 30d, 1000d);
            if (!SetProperty(ref field, normalized))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedPollingRateText));
        }
    } = 250;

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        ThrowIfDisposed();
        await profileSession.EnsureInitializedAsync();

        SelectedLanguage = SupportedLanguages
            .FirstOrDefault(l => l.Code == profileSession.Settings.SelectedCulture)
            ?? SupportedLanguages.FirstOrDefault();

        // Apply the saved theme (defaults to CyberBlue if not stored)
        var savedThemeKey = profileSession.CurrentProfile.Ui.Theme;
        if (Enum.TryParse<AppThemeKind>(savedThemeKey, true, out var savedKind))
        {
            SelectedTheme = ThemeOptions.FirstOrDefault(t => t.Kind == savedKind)
                            ?? ThemeOptions.FirstOrDefault();
        }

        SyncDashboardSelectionsFromProfile();
        MappingEditor.LoadFromProfile(profileSession.CurrentProfile);
        RebuildControlConfigurationCards();
        await RefreshAvailableProfilesAsync();
        RefreshControllerInventory();

        AboutText = BuildAboutText();
        aboutTextDirty = false;

        ProfileJson = profileSession.SerializeCurrentProfile();
        jsonDirty = false;

        ProfileName = profileSession.CurrentProfile.Name;

        RefreshLocalizedText();
        ResetSelectionInspector();
        await RefreshRuntimeAsync();
    }

    // ─── Main refresh — called every 33 ms ────────────────────────────────────

    public Task RefreshRuntimeAsync()
    {
        if (disposed)
        {
            return Task.CompletedTask;
        }

        var snapshot = runtimeSnapshotStore.Current;
        var profile  = profileSession.CurrentProfile;
        var physicalStyle = SelectedPhysicalStyle?.Style ?? profile.Ui.PhysicalControllerStyle;
        var virtualStyle  = SelectedVirtualStyle?.Style  ?? profile.Ui.VirtualControllerStyle;

        // ── Fast path: always ─────────────────────────────────────────────────
        PhysicalController.Update("physical", PhysicalInputLabel, snapshot.PhysicalSnapshot, physicalStyle);
        VirtualController.Update("virtual",   VirtualOutputLabel,  snapshot.VirtualSnapshot,  virtualStyle);

        StatusText =
            $"{localizationService["StatusPrefix"]} {snapshot.InputProvider} → {snapshot.OutputProvider}  ·  " +
            $"{localizationService["ActiveProfilePrefix"]} {profile.Name}";

        // Diagnostics update on every tick (fast path) — the build is cheap
        // (one StringBuilder pass with no I/O) so it does not cause performance issues.
        DiagnosticsText = BuildDiagnostics(snapshot);

        if (!string.IsNullOrWhiteSpace(selectedControlKey))
        {
            ApplySelection(selectedControlKey, snapshot);
        }

        refreshTick++;

        // ── Medium path: ~0.5 s ───────────────────────────────────────────────
        if (refreshTick % MedPathEvery == 0 || refreshTick == 1)
        {
            RefreshControllerInventory();
            RuntimeNotesText = BuildRuntimeNotes(snapshot);
        }

        // ── Slow path: ~3 s ───────────────────────────────────────────────────
        if (refreshTick % SlowPathEvery == 0 || jsonDirty)
        {
            PhysicalStateJson = JsonSerializer.Serialize(
                snapshot.PhysicalSnapshot, ProfileJsonOptions.Default);
            VirtualStateJson  = JsonSerializer.Serialize(
                snapshot.VirtualSnapshot, ProfileJsonOptions.Default);
            jsonDirty = false;
        }

        // ── Dirty-flag paths ──────────────────────────────────────────────────
        if (ruleSummaryDirty)
        {
            RuleSummary = string.Join(
                Environment.NewLine,
                profile.Rules.Select(r => $"- {r.Name} [{r.GetType().Name}]"));
            ruleSummaryDirty = false;
        }

        if (aboutTextDirty)
        {
            AboutText = BuildAboutText();
            aboutTextDirty = false;
        }

        return Task.CompletedTask;
    }

    // ─── Dashboard preference command ─────────────────────────────────────────

    private async Task ApplyDashboardPreferencesAsync()
    {
        if (SelectedInputProvider is null || SelectedPhysicalStyle is null || SelectedVirtualStyle is null)
        {
            return;
        }

        try
        {
            var outputKey = SelectedOutputProvider?.Key
                ?? profileSession.CurrentProfile.OutputProvider;

            var themeKey = SelectedTheme?.Kind.ToString() ?? "CyberBlue";

            var profile = profileSession.CurrentProfile with
            {
                InputProvider = SelectedInputProvider.Key,
                OutputProvider = outputKey,
                PollingRateHz = (int)SelectedPollingRateHz,
                PreferredInputDeviceId = SelectedController?.Id ?? string.Empty,
                Ui = profileSession.CurrentProfile.Ui with
                {
                    PhysicalControllerStyle = SelectedPhysicalStyle.Style,
                    VirtualControllerStyle  = SelectedVirtualStyle.Style,
                    Theme = themeKey
                }
            };

            await profileSession.SaveCurrentProfileAsync(profile);
            ProfileJson      = profileSession.SerializeCurrentProfile();
            jsonDirty        = false;
            ruleSummaryDirty = true;
            await RefreshRuntimeAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to apply dashboard preferences.");
            StatusText = $"Apply failed: {exception.Message}";
        }
    }

    // ─── Save / reset / duplicate / rename helpers ────────────────────────────

    private async Task SaveProfileAsync()
    {
        try
        {
            await profileSession.SaveCurrentProfileAsync(profileSession.CurrentProfile);
            ProfileJson      = profileSession.SerializeCurrentProfile();
            jsonDirty        = false;
            ruleSummaryDirty = true;
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Profile saved.");
            }
            await RefreshRuntimeAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to save profile.");
            StatusText = $"Save failed: {exception.Message}";
        }
    }

    private async Task ResetProfileAsync()
    {
        isSwitchingProfile = true;
        try
        {
            await profileSession.ResetToDefaultAsync();
            MappingEditor.LoadFromProfile(profileSession.CurrentProfile);
            RebuildControlConfigurationCards();
            await RefreshAvailableProfilesAsync();
            SyncDashboardSelectionsFromProfile();
            RefreshControllerInventory();
            ProfileJson      = profileSession.SerializeCurrentProfile();
            jsonDirty        = false;
            ruleSummaryDirty = true;
            ProfileName      = profileSession.CurrentProfile.Name;
            await RefreshRuntimeAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to reset profile.");
            StatusText = $"Reset failed: {exception.Message}";
        }
        finally
        {
            isSwitchingProfile = false;
        }
    }

    private async Task CreateProfileAsync()
    {
        isSwitchingProfile = true;
        try
        {
            var profile = await profileSession.CreateNewProfileAsync($"Profile {DateTime.Now:HHmmss}");
            await RefreshAvailableProfilesAsync();
            ProfileName = profile.Name;
            var target = AvailableProfiles.FirstOrDefault(o => o.Id == profile.Id);
            if (target is not null)
            {
                selectedProfileOption = target;
                OnPropertyChanged(nameof(SelectedProfileOption));
                OnPropertyChanged(nameof(SelectedProfileSummary));
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to create profile.");
            StatusText = $"Create profile failed: {exception.Message}";
        }
        finally
        {
            isSwitchingProfile = false;
        }
    }

    private async Task DuplicateProfileAsync()
    {
        isSwitchingProfile = true;
        try
        {
            var copy = await profileSession.DuplicateCurrentProfileAsync($"{profileSession.CurrentProfile.Name} Copy");
            await RefreshAvailableProfilesAsync();
            ProfileName = copy.Name;
            var target = AvailableProfiles.FirstOrDefault(o => o.Id == copy.Id);
            if (target is not null)
            {
                selectedProfileOption = target;
                OnPropertyChanged(nameof(SelectedProfileOption));
                OnPropertyChanged(nameof(SelectedProfileSummary));
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to duplicate profile.");
            StatusText = $"Duplicate profile failed: {exception.Message}";
        }
        finally
        {
            isSwitchingProfile = false;
        }
    }

    private async Task ImportProfileAsync()
    {
        try
        {
            var json = await profileFileDialogService.ImportProfileJsonAsync();
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            isSwitchingProfile = true;
            try
            {
                await profileSession.ImportProfileAsync(json);
                await RefreshAvailableProfilesAsync();
                ProfileJson      = profileSession.SerializeCurrentProfile();
                jsonDirty        = false;
                ruleSummaryDirty = true;
                RebuildControlConfigurationCards();
                ProfileName = profileSession.CurrentProfile.Name;
                await RefreshRuntimeAsync();
            }
            finally
            {
                isSwitchingProfile = false;
            }
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to import profile.");
            StatusText = $"Import failed: {exception.Message}";
        }
    }

    private async Task ExportProfileAsync()
    {
        try
        {
            var name = SanitizeFileName(profileSession.CurrentProfile.Name);
            _ = await profileFileDialogService.ExportProfileJsonAsync($"{name}.json",
                profileSession.SerializeCurrentProfile());
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to export profile.");
            StatusText = $"Export failed: {exception.Message}";
        }
    }

    private async Task RenameProfileAsync()
    {
        var newName = ProfileName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newName) ||
            string.Equals(newName, profileSession.CurrentProfile.Name, StringComparison.Ordinal))
        {
            return;
        }

        isSwitchingProfile = true;
        try
        {
            var profile = profileSession.CurrentProfile with { Name = newName };
            await profileSession.SaveCurrentProfileAsync(profile);
            ProfileJson      = profileSession.SerializeCurrentProfile();
            jsonDirty        = false;
            ruleSummaryDirty = true;
            await RefreshAvailableProfilesAsync();
            var target = AvailableProfiles.FirstOrDefault(o => o.Id == profile.Id);
            if (target is not null)
            {
                selectedProfileOption = target;
                OnPropertyChanged(nameof(SelectedProfileOption));
                OnPropertyChanged(nameof(SelectedProfileSummary));
            }
            await RefreshRuntimeAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to rename profile.");
            StatusText = $"Rename failed: {exception.Message}";
        }
        finally
        {
            isSwitchingProfile = false;
        }
    }

    // ─── Dashboard sync ───────────────────────────────────────────────────────

    private void SyncDashboardSelectionsFromProfile()
    {
        var profile = profileSession.CurrentProfile;

        SelectedInputProvider = InputProviderOptions
            .FirstOrDefault(o => o.Key == profile.InputProvider)
            ?? InputProviderOptions.FirstOrDefault();

        SelectedOutputProvider = OutputProviderOptions
            .FirstOrDefault(o => o.Key == profile.OutputProvider)
            ?? OutputProviderOptions.FirstOrDefault();

        SelectedPhysicalStyle = ControllerStyleOptions
            .FirstOrDefault(o => o.Style == profile.Ui.PhysicalControllerStyle)
            ?? ControllerStyleOptions.FirstOrDefault();

        SelectedVirtualStyle = ControllerStyleOptions
            .FirstOrDefault(o => o.Style == profile.Ui.VirtualControllerStyle)
            ?? ControllerStyleOptions.FirstOrDefault();

        SelectedPollingRateHz = profile.PollingRateHz;

        if (Enum.TryParse<AppThemeKind>(profile.Ui.Theme, true, out var savedKind))
        {
            var themeOpt = ThemeOptions.FirstOrDefault(t => t.Kind == savedKind);
            if (themeOpt is not null && themeOpt != selectedTheme)
            {
                selectedTheme = themeOpt;
                OnPropertyChanged(nameof(SelectedTheme));
                AppThemeService.Apply(savedKind);
            }
        }
    }

    private async Task RefreshAvailableProfilesAsync()
    {
        var profiles = await profileSession.ListProfilesAsync();
        AvailableProfiles = [.. profiles
            .Select(p => new ProfileOption(p.Id, p.Name, p.Id))
            .OrderBy(p => p.Label, StringComparer.OrdinalIgnoreCase)];

        var current = AvailableProfiles.FirstOrDefault(o => o.Id == profileSession.CurrentProfile.Id)
            ?? AvailableProfiles.FirstOrDefault();

        if (!ReferenceEquals(selectedProfileOption, current))
        {
            selectedProfileOption = current;
            OnPropertyChanged(nameof(SelectedProfileOption));
            OnPropertyChanged(nameof(SelectedProfileSummary));
        }
    }

    private async Task SwitchToProfileAsync(string profileId)
    {
        try
        {
            isSwitchingProfile = true;
            await profileSession.SwitchToProfileAsync(profileId);
            MappingEditor.LoadFromProfile(profileSession.CurrentProfile);
            RebuildControlConfigurationCards();
            ProfileJson      = profileSession.SerializeCurrentProfile();
            jsonDirty        = false;
            ruleSummaryDirty = true;
            ProfileName      = profileSession.CurrentProfile.Name;
            await RefreshRuntimeAsync();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to switch profile to {ProfileId}.", profileId);
            StatusText = $"Profile switch failed: {exception.Message}";
        }
        finally
        {
            isSwitchingProfile = false;
        }
    }

    private void RefreshControllerInventory()
    {
        if (disposed)
        {
            return;
        }

        var profile = profileSession.CurrentProfile;
        var devices = inputDeviceCatalog.Devices;

        var options = new List<DetectedControllerOption>
        {
            new(string.Empty,
                localizationService["AutomaticControllerSelection"],
                localizationService["AutomaticControllerSelectionDescription"])
        };

        options.AddRange(devices.Select(d =>
            new DetectedControllerOption(d.Id, d.DisplayName,
                string.IsNullOrWhiteSpace(d.HardwareId) ? d.DisplayName : d.HardwareId)));

        AvailableControllers = options;

        var preferredId = selectedController?.Id;
        if (string.IsNullOrWhiteSpace(preferredId))
        {
            preferredId = inputDeviceCatalog.SelectedDeviceId;
        }

        if (string.IsNullOrWhiteSpace(preferredId))
        {
            preferredId = profile.PreferredInputDeviceId;
        }

        var nextSelected = AvailableControllers
            .FirstOrDefault(o => string.Equals(o.Id, preferredId, StringComparison.OrdinalIgnoreCase))
            ?? AvailableControllers.First();

        if (selectedController != nextSelected)
        {
            selectedController = nextSelected;
            OnPropertyChanged(nameof(SelectedController));
            OnPropertyChanged(nameof(SelectedControllerSummary));
        }

        inputDeviceCatalog.SetSelectedDevice(
            string.IsNullOrWhiteSpace(nextSelected.Id) ? null : nextSelected.Id);

        ControllerInventoryText = devices.Count switch
        {
            0 => localizationService["NoControllersDetected"],
            1 => string.Format(localizationService["ControllersDetectedSingle"], devices[0].DisplayName),
            _ => string.Format(localizationService["ControllersDetectedMultiple"], devices.Count)
        };

        ProviderStatusText = inputDeviceCatalog.ProviderStatus;
    }

    private void RebuildControlConfigurationCards()
    {
        var grouped = profileSession.CurrentProfile.Rules
            .GroupBy(ControlRuleMatcher.GetPrimarySelectionKey)
            .OrderBy(g => ControlRuleMatcher.GetTitle(g.Key), StringComparer.OrdinalIgnoreCase)
            .Select(g => new ControlConfigurationCardViewModel(
                g.Key,
                ControlRuleMatcher.GetTitle(g.Key),
                ControlRuleMatcher.GetHint(g.Key),
                [.. g.Select(ControlRuleMatcher.CreateEntry)]))
            .ToArray();

        ControlConfigurationCards = grouped;
        OnPropertyChanged(nameof(HasControlConfigurationCards));
        OnPropertyChanged(nameof(HasNoControlConfigurationCards));
    }

    private void OpenControlEditor(string? selectionKey)
    {
        if (string.IsNullOrWhiteSpace(selectionKey))
        {
            return;
        }

        var dialogViewModel = new ControlMappingDialogViewModel(
            selectionKey, profileSession.CurrentProfile, loggerFactory);

        dialogViewModel.MergedRulesChanged += OnDialogMergedRulesChanged;
        ControlMappingRequested?.Invoke(this, new ControlMappingRequestedEventArgs(dialogViewModel));
    }

    private void OnDialogMergedRulesChanged(object? sender, IReadOnlyList<MappingRule> mergedRules)
    {
        _ = SaveRulesAsync(mergedRules);
    }

    private void OnMappingRulesChanged(object? sender, IReadOnlyList<MappingRule> rules)
    {
        _ = SaveRulesAsync(rules);
    }

    private async Task SaveRulesAsync(IReadOnlyList<MappingRule> rules)
    {
        if (disposed)
        {
            return;
        }

        await rulesSaveGate.WaitAsync();
        try
        {
            var profile = profileSession.CurrentProfile with { Rules = [.. rules] };
            await profileSession.SaveCurrentProfileAsync(profile);
            ProfileJson      = profileSession.SerializeCurrentProfile();
            jsonDirty        = false;
            ruleSummaryDirty = true;
            RebuildControlConfigurationCards();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to save mapping rules.");
            StatusText = $"Rule save failed: {exception.Message}";
        }
        finally
        {
            _ = rulesSaveGate.Release();
        }
    }

    // ─── Text builders ────────────────────────────────────────────────────────

    private string BuildDiagnostics(RuntimeSnapshot snapshot)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine($"--- {localizationService["DiagnosticsLabel"]}  [{DateTimeOffset.Now:HH:mm:ss.fff}] ---");
        _ = sb.AppendLine($"- Logs: {AppPaths.LogsDirectory}");
        _ = sb.AppendLine($"- Last input frame: {(snapshot.LastUpdated == default ? "no data yet" : snapshot.LastUpdated.LocalDateTime.ToString("HH:mm:ss.fff"))}");
        _ = sb.AppendLine($"- Dashboard refresh: {runtimeOptions.DashboardRefreshHz} Hz target");
        _ = sb.AppendLine($"- Input provider (requested): {profileSession.CurrentProfile.InputProvider}");
        _ = sb.AppendLine($"- Input provider (effective): {snapshot.InputProvider}");
        _ = sb.AppendLine($"- Output provider: {snapshot.OutputProvider}");
        _ = sb.AppendLine($"- ViGEm enabled: {runtimeOptions.EnableViGEm}");
        _ = sb.AppendLine($"- Controllers detected: {inputDeviceCatalog.Devices.Count}");
        _ = sb.AppendLine($"- Provider status: {inputDeviceCatalog.ProviderStatus}");
        _ = sb.AppendLine($"- Experimental GameInput: {runtimeOptions.EnableExperimentalGameInput}");
        _ = sb.AppendLine();
        _ = sb.AppendLine(localizationService["ProviderPlanHeading"]);
        _ = sb.Append(providerSummary);
        return sb.ToString();
    }

    private string BuildAboutText()
    {
        return string.Join(Environment.NewLine,
        [
            localizationService["AboutBodyLine1"],
            localizationService["AboutBodyLine2"],
            localizationService["AboutBodyLine3"],
            string.Empty,
            localizationService["AboutBodyLine4"]
        ]);
    }

    private string BuildRuntimeNotes(RuntimeSnapshot snapshot)
    {
        var lines = new List<string>
        {
            $"- {ProviderStatusText}",
            $"- {ControllerInventoryText}"
        };

        var inputProvider = profileSession.CurrentProfile.InputProvider;

        if (inputProvider.Equals("demo", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("- Demo preview is active. The dashboard animates intentionally without hardware.");
        }

        if (inputProvider.Equals("xinput", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("- XInput is active. Only XInput-compatible (Xbox-class) controllers are enumerated.");
        }

        if (IsSdlProvider(inputProvider))
        {
            lines.Add("- SDL3 unified input is active. Uses standardised gamepad mappings with joystick fallback.");
        }

        if (inputProvider.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add("- Live input is disabled for this profile.");
        }

        lines.AddRange(snapshot.Notes.Select(n => $"- {n}"));
        return string.Join(Environment.NewLine, lines.Distinct(StringComparer.Ordinal));
    }

    // ─── Selection inspector ──────────────────────────────────────────────────

    private void OnControllerElementSelected(string selectionKey)
    {
        selectedControlKey = selectionKey;
        ApplySelection(selectionKey, runtimeSnapshotStore.Current);

        if (selectionKey.StartsWith("physical:", StringComparison.OrdinalIgnoreCase))
        {
            OpenControlEditor(selectionKey);
        }
    }

    private void ApplySelection(string selectionKey, RuntimeSnapshot snapshot)
    {
        var sep = selectionKey.IndexOf(':');
        if (sep < 0)
        {
            return;
        }

        var scope      = selectionKey[..sep];
        var elementKey = selectionKey[(sep + 1)..];
        var source     = scope.Equals("virtual", StringComparison.OrdinalIgnoreCase)
            ? snapshot.VirtualSnapshot
            : snapshot.PhysicalSnapshot;

        SelectedControlTitle = $"{FormatScope(scope)} · {FormatElementName(elementKey)}";
        SelectedControlValue = BuildCurrentValue(elementKey, source);
        SelectedControlRules = BuildRuleMatches(elementKey);
        SelectedControlHint  = BuildSelectionHint(elementKey);
    }

    private static string BuildCurrentValue(string elementKey, ControllerSnapshot snapshot)
    {
        if (TryParseButton(elementKey, out var button))
        {
            return snapshot.IsPressed(button) ? "Current state: pressed" : "Current state: released";
        }

        return elementKey.StartsWith("LeftTrigger", StringComparison.OrdinalIgnoreCase)
            ? $"Current value: {snapshot.LeftTrigger:P0}"
            : elementKey.StartsWith("RightTrigger", StringComparison.OrdinalIgnoreCase)
            ? $"Current value: {snapshot.RightTrigger:P0}"
            : elementKey.StartsWith("LeftStick", StringComparison.OrdinalIgnoreCase)
            ? $"Current vector: {snapshot.LeftStick.X:0.00}, {snapshot.LeftStick.Y:0.00}"
            : elementKey.StartsWith("RightStick", StringComparison.OrdinalIgnoreCase)
            ? $"Current vector: {snapshot.RightStick.X:0.00}, {snapshot.RightStick.Y:0.00}"
            : elementKey.Equals("Touchpad", StringComparison.OrdinalIgnoreCase)
            ? snapshot.TouchContactCount switch
            {
                <= 0 => "Current state: no active touches",
                1    => "Current state: 1 touch contact",
                _    => $"Current state: {snapshot.TouchContactCount} touch contacts"
            }
            : "Current state unavailable.";
    }

    private string BuildRuleMatches(string elementKey)
    {
        var selectionKey = ControlRuleMatcher.EnsurePhysicalSelectionKey(elementKey);
        var lines = profileSession.CurrentProfile.Rules
            .Where(r => ControlRuleMatcher.Matches(selectionKey, r))
            .Select(r => $"- {r.Name}: {ControlRuleMatcher.CreateEntry(r).Value}")
            .ToArray();

        return lines.Length == 0
            ? "No rule is using this control yet."
            : string.Join(Environment.NewLine, lines);
    }

    private static string BuildSelectionHint(string elementKey)
    {
        return ControlRuleMatcher.GetHint(elementKey);
    }

    private void ResetSelectionInspector()
    {
        SelectedControlTitle = "Click any control on the physical controller surface.";
        SelectedControlValue = "The inspector shows the current live state of the selected element.";
        SelectedControlRules = "Matching rules for the selected control will appear here.";
        SelectedControlHint  = "Clicking a physical control opens its dedicated configuration window.";
    }

    // ─── Localised text refresh ───────────────────────────────────────────────

    private void RefreshLocalizedText()
    {
        if (disposed)
        {
            return;
        }

        WindowTitle    = localizationService["WindowTitle"];
        aboutTextDirty = true;

        OnPropertyChanged(nameof(DashboardTabLabel));
        OnPropertyChanged(nameof(ProfilesTabLabel));
        OnPropertyChanged(nameof(DiagnosticsTabLabel));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(ThemeLabel));
        OnPropertyChanged(nameof(SaveProfileButtonLabel));
        OnPropertyChanged(nameof(ResetProfileButtonLabel));
        OnPropertyChanged(nameof(PhysicalInputLabel));
        OnPropertyChanged(nameof(VirtualOutputLabel));
        OnPropertyChanged(nameof(RuleSummaryLabel));
        OnPropertyChanged(nameof(ProfileEditorLabel));
        OnPropertyChanged(nameof(DiagnosticsLabel));
        OnPropertyChanged(nameof(DashboardSettingsLabel));
        OnPropertyChanged(nameof(DashboardSubtitle));
        OnPropertyChanged(nameof(InputProviderLabel));
        OnPropertyChanged(nameof(OutputProviderLabel));
        OnPropertyChanged(nameof(ControllerLabel));
        OnPropertyChanged(nameof(ControllerInventoryLabel));
        OnPropertyChanged(nameof(ProviderStatusLabel));
        OnPropertyChanged(nameof(PollingRateLabel));
        OnPropertyChanged(nameof(PhysicalStyleLabel));
        OnPropertyChanged(nameof(VirtualStyleLabel));
        OnPropertyChanged(nameof(ApplyDashboardSettingsButtonLabel));
        OnPropertyChanged(nameof(SelectedControlLabel));
        OnPropertyChanged(nameof(RuntimeNotesLabel));
        OnPropertyChanged(nameof(RawPhysicalLabel));
        OnPropertyChanged(nameof(RawVirtualLabel));
        OnPropertyChanged(nameof(ProfileComboLabel));
        OnPropertyChanged(nameof(ProfileToolbarSubtitle));
        OnPropertyChanged(nameof(ProfileNameLabel));
        OnPropertyChanged(nameof(ProfileNameWatermark));
        OnPropertyChanged(nameof(RenameProfileButtonLabel));
        OnPropertyChanged(nameof(CreateProfileButtonLabel));
        OnPropertyChanged(nameof(DuplicateProfileButtonLabel));
        OnPropertyChanged(nameof(ImportProfileButtonLabel));
        OnPropertyChanged(nameof(ExportProfileButtonLabel));
        OnPropertyChanged(nameof(MappingOverviewLabel));
        OnPropertyChanged(nameof(MappingOverviewSubtitle));
        OnPropertyChanged(nameof(OpenControlEditorButtonLabel));
        OnPropertyChanged(nameof(NoMappingCardsText));
        OnPropertyChanged(nameof(SelectedControllerSummary));
        OnPropertyChanged(nameof(SelectedProfileSummary));
        OnPropertyChanged(nameof(HasControlConfigurationCards));
        OnPropertyChanged(nameof(HasNoControlConfigurationCards));

        RefreshControllerInventory();
    }

    // ─── Event handlers ───────────────────────────────────────────────────────

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        PostUiRefresh(RefreshLocalizedText);
    }

    private void OnProfileChanged(object? sender, EventArgs e)
    {
        if (isSwitchingProfile)
        {
            return;
        }

        PostUiRefresh(() =>
        {
            SyncDashboardSelectionsFromProfile();
            MappingEditor.LoadFromProfile(profileSession.CurrentProfile);
            RebuildControlConfigurationCards();
            RefreshControllerInventory();
            _ = RefreshAvailableProfilesAsync();
            ProfileJson      = profileSession.SerializeCurrentProfile();
            jsonDirty        = false;
            ruleSummaryDirty = true;
            ProfileName      = profileSession.CurrentProfile.Name;
            OnPropertyChanged(nameof(SelectedProfileSummary));
            OnPropertyChanged(nameof(HasControlConfigurationCards));
            OnPropertyChanged(nameof(HasNoControlConfigurationCards));
        });
    }

    private void PostUiRefresh(Action? beforeRefresh = null)
    {
        if (disposed)
        {
            return;
        }

        Dispatcher.UIThread.Post(async () =>
        {
            if (disposed)
            {
                return;
            }

            try
            {
                beforeRefresh?.Invoke();
                await RefreshRuntimeAsync();
            }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Deferred dashboard refresh failed.");
            }
        });
    }

    // ─── Static factory helpers ───────────────────────────────────────────────

    private static IReadOnlyList<AppThemeOption> CreateThemeOptions()
    {
        return
        [
            new AppThemeOption(AppThemeKind.CyberBlue,      "Cyber Blue"),
            new AppThemeOption(AppThemeKind.MidnightPurple, "Midnight Purple"),
            new AppThemeOption(AppThemeKind.NeonGreen,      "Neon Green"),
            new AppThemeOption(AppThemeKind.SolarRed,       "Solar Red"),
            new AppThemeOption(AppThemeKind.Light,          "Light"),
        ];
    }

    private static IReadOnlyList<InputProviderOption> CreateInputProviderOptions()
    {
        return
        [
            new InputProviderOption("xinput", "XInput",
                "Windows native XInput driver. Enumerates up to 4 Xbox-compatible controllers."),
            new InputProviderOption("sdl", "SDL3 unified input",
                "Cross-platform SDL3 gamepad mapping plus joystick fallback."),
            new InputProviderOption("demo", "Demo preview",
                "Animated preview source for UI testing — no hardware required."),
            new InputProviderOption("none", "No live input",
                "Turns off live input and leaves the dashboard idle."),
        ];
    }

    private static IReadOnlyList<OutputProviderOption> CreateOutputProviderOptions()
    {
        return
        [
            new OutputProviderOption("vigem-xbox360", "ViGEm Xbox 360",
                "Virtual Xbox 360 controller via ViGEm Bus. Requires ViGEm Bus driver."),
            new OutputProviderOption("vigem-ds4", "ViGEm DualShock 4",
                "Virtual DualShock 4 controller via ViGEm Bus. Requires ViGEm Bus driver."),
            new OutputProviderOption("vigem-ds5", "ViGEm DualSense (DS5)",
                "Virtual DualSense controller via ViGEm Bus. Requires ViGEm Bus driver v1.22+."),
            new OutputProviderOption("preview", "Preview only",
                "Shows the transformed output in the dashboard without creating a virtual device."),
        ];
    }

    private static IReadOnlyList<ControllerStyleOption> CreateControllerStyleOptions()
    {
        return
        [
            new ControllerStyleOption(ControllerVisualStyle.Auto,         "Auto"),
            new ControllerStyleOption(ControllerVisualStyle.Xbox,         "Xbox"),
            new ControllerStyleOption(ControllerVisualStyle.PlayStation4, "PlayStation 4"),
            new ControllerStyleOption(ControllerVisualStyle.PlayStation5, "PlayStation 5"),
            new ControllerStyleOption(ControllerVisualStyle.None,         "Minimal"),
        ];
    }

    // ─── Misc helpers ─────────────────────────────────────────────────────────

    private static string FormatScope(string scope)
    {
        return scope.Equals("virtual", StringComparison.OrdinalIgnoreCase) ? "Virtual output" : "Physical input";
    }

    private static string FormatElementName(string k)
    {
        return k.Replace("LeftStick",    "Left stick",    StringComparison.OrdinalIgnoreCase)
                .Replace("RightStick",   "Right stick",   StringComparison.OrdinalIgnoreCase)
                .Replace("LeftTrigger",  "Left trigger",  StringComparison.OrdinalIgnoreCase)
                .Replace("RightTrigger", "Right trigger", StringComparison.OrdinalIgnoreCase)
                .Replace(".Analog", " · Analog", StringComparison.OrdinalIgnoreCase)
                .Replace(".Button", " · Click",  StringComparison.OrdinalIgnoreCase)
                .Replace(".Up",     " · Up",     StringComparison.OrdinalIgnoreCase)
                .Replace(".Down",   " · Down",   StringComparison.OrdinalIgnoreCase)
                .Replace(".Left",   " · Left",   StringComparison.OrdinalIgnoreCase)
                .Replace(".Right",  " · Right",  StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseButton(string elementKey, out ButtonId button)
    {
        return ControlRuleMatcher.TryResolveButtonId(elementKey, out button);
    }

    private static bool IsSdlProvider(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return true;
        }

        var n = id.Trim().ToLowerInvariant();
        return n is "sdl" or "sdlgamepad" or "sdl-unified" or "sdl3" or "sdl-unified-gamepad";
    }

    private static string SanitizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "profile";
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder      = new StringBuilder(value.Length);

        foreach (var ch in value.Trim())
        {
            _ = builder.Append(Array.IndexOf(invalidChars, ch) >= 0 ? '_' : ch);
        }

        var sanitized = builder.ToString().Trim().Trim('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "profile" : sanitized;
    }

    // ─── Dispose ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        localizationService.CultureChanged -= OnCultureChanged;
        profileSession.Changed             -= OnProfileChanged;
        MappingEditor.RulesChanged         -= OnMappingRulesChanged;
        MappingEditor.Dispose();
        rulesSaveGate.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, nameof(ShellViewModel));
    }
}
