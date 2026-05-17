using System.Reflection;
using System.Text;
using System.Text.Json;
using Autofire.App.Services;
using Autofire.App.Views;
using Autofire.Core.Enums;
using Autofire.Core.Models;
using Autofire.Infrastructure.Configuration;
using Autofire.Infrastructure.Localization;
using Autofire.Infrastructure.Profiles;
using Autofire.Infrastructure.Runtime;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
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
    // Refresh cadence, expressed in UI ticks (the dispatcher fires at ~30 Hz, see ShellWindow.OnOpened).
    //   Fast path  — every tick (~33 ms).  Visible button states + diagnostics text.
    //   Medium     — every 15 ticks (~500 ms).  Controller-inventory rebuild + runtime notes.
    //   Slow       — every 15 ticks (~500 ms).  JSON re-serialisation of the raw snapshots.
    //
    // The JSON path used to fire every 90 ticks (~3 s), which made the "Raw physical/virtual
    // snapshot" debug panes feel frozen. 500 ms keeps them legibly responsive without the
    // serialisation cost showing up in profilers.
    private const int SlowPathEvery = 1;
    private const int MedPathEvery  = 1;
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
    private readonly IServiceProvider serviceProvider;
    private readonly AppRuntimeOptions runtimeOptions;
    private readonly SemaphoreSlim rulesSaveGate = new(1, 1);

    private LanguageOption? selectedLanguage;
    private AppThemeOption? selectedTheme;
    private DetectedControllerOption? selectedController;
    private ProfileOption? selectedProfileOption;
    private string providerSummary = string.Empty;
    private string? selectedControlKey;
    private bool isSwitchingProfile;

    /// <summary>
    /// Set <c>true</c> while <see cref="SelectedLanguage"/>'s setter is
    /// running so the <see cref="ProfileSession.Changed"/> event raised
    /// by <see cref="ProfileSession.SetCultureAsync"/> doesn't trigger
    /// the heavy profile-sync chain. The profile didn't actually change,
    /// only the persisted UI culture did, and the sync chain has been
    /// observed to flicker the language combo back to its previous
    /// value.
    /// </summary>
    private bool isChangingCulture;
    private bool disposed;

    /// <summary>
    /// Latest theme-variant id the user picked from the physical
    /// panel's variant ComboBox. Null until they pick something; cleared
    /// after a successful "Apply Dashboard Preferences" save folds it
    /// into the profile.
    /// </summary>
    private string? pendingPhysicalVariantPick;

    /// <summary>
    /// Sibling to <see cref="pendingPhysicalVariantPick"/> for the
    /// virtual panel.
    /// </summary>
    private string? pendingVirtualVariantPick;

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
        ILogger<ShellViewModel> logger,
        IServiceProvider serviceProvider)
    {
        this.profileSession = profileSession;
        this.runtimeSnapshotStore = runtimeSnapshotStore;
        this.localizationService = localizationService;
        this.inputDeviceCatalog = inputDeviceCatalog;
        this.profileFileDialogService = profileFileDialogService;
        this.runtimeOptions = runtimeOptions.Value;
        this.loggerFactory = loggerFactory;
        this.logger = logger;
        this.serviceProvider = serviceProvider;

        SaveProfileCommand               = new AsyncRelayCommand(SaveProfileAsync);
        ResetProfileCommand              = new AsyncRelayCommand(ResetProfileAsync);
        ApplyDashboardPreferencesCommand = new AsyncRelayCommand(ApplyDashboardPreferencesAsync);
        CreateProfileCommand             = new AsyncRelayCommand(CreateProfileAsync);
        DuplicateProfileCommand          = new AsyncRelayCommand(DuplicateProfileAsync);
        ImportProfileCommand             = new AsyncRelayCommand(ImportProfileAsync);
        ExportProfileCommand             = new AsyncRelayCommand(ExportProfileAsync);
        RenameProfileCommand             = new AsyncRelayCommand(RenameProfileAsync);
        DeleteProfileCommand             = new AsyncRelayCommand(DeleteProfileAsync, CanDeleteProfile);
        OpenControlEditorCommand         = new RelayCommand<string>(OpenControlEditor);
        OpenSettingsCommand              = new AsyncRelayCommand(OpenSettingsAsync);

        SupportedLanguages     = localizationService.SupportedLanguages;
        ThemeOptions           = CreateThemeOptions();
        InputProviderOptions   = CreateInputProviderOptions();
        OutputProviderOptions  = CreateOutputProviderOptions();
        ControllerStyleOptions = CreateControllerStyleOptions();
        MappingEditor          = new MappingEditorViewModel(loggerFactory.CreateLogger<MappingEditorViewModel>(), localizationService);
        MappingEditor.RulesChanged += OnMappingRulesChanged;

        selectedLanguage = SupportedLanguages
            .FirstOrDefault(l => l.Code == localizationService.CurrentCulture)
            ?? SupportedLanguages.FirstOrDefault();

        selectedTheme = ThemeOptions.FirstOrDefault(t => t.Kind == AppThemeKind.CyberBlue)
            ?? ThemeOptions.FirstOrDefault();

        PhysicalController = new ControllerVisualStateViewModel(OnControllerElementSelected, localizationService);
        VirtualController  = new ControllerVisualStateViewModel(OnControllerElementSelected, localizationService);

        // Mark each panel's render mode. Physical = base image only
        // (the actual controller model, no live overlays); Virtual =
        // full live render with active button highlights and stick
        // deflection. The flag is read by the ThemeSurface; programmatic
        // XAML art is unaffected.
        PhysicalController.SetPanelKind(isPhysical: true);
        VirtualController.SetPanelKind(isPhysical: false);

        // Remember user theme picks so they survive a profile save.
        // We don't immediately call SaveProfile here — that would be
        // surprising side-effect behaviour; instead we stash the choice
        // in `pendingThemeVariantPick*` fields and ApplyDashboardPreferences
        // folds them into the next profile write. Hitting Apply (or any
        // other profile save) commits the change to disk.
        PhysicalController.ThemeVariantUserSelected += (_, pick) =>
            pendingPhysicalVariantPick = pick?.Id;
        VirtualController.ThemeVariantUserSelected += (_, pick) =>
            pendingVirtualVariantPick = pick?.Id;

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
    public IAsyncRelayCommand DeleteProfileCommand             { get; }
    public IAsyncRelayCommand OpenSettingsCommand              { get; }
    public IRelayCommand<string> OpenControlEditorCommand      { get; }

    public event EventHandler<ControlMappingRequestedEventArgs>? ControlMappingRequested;

    // ─── Collections ──────────────────────────────────────────────────────────

    public IReadOnlyList<LanguageOption> SupportedLanguages    { get; }
    public IReadOnlyList<AppThemeOption> ThemeOptions
    {
        get => themeOptions;
        private set => SetProperty(ref themeOptions, value);
    }
    public IReadOnlyList<InputProviderOption> InputProviderOptions
    {
        get => inputProviderOptions;
        private set => SetProperty(ref inputProviderOptions, value);
    }
    public IReadOnlyList<OutputProviderOption> OutputProviderOptions
    {
        get => outputProviderOptions;
        private set => SetProperty(ref outputProviderOptions, value);
    }
    public IReadOnlyList<ControllerStyleOption> ControllerStyleOptions
    {
        get => controllerStyleOptions;
        private set => SetProperty(ref controllerStyleOptions, value);
    }

    // Backing fields for the rebuilt-on-culture-change option lists above.
    // Kept private so the only writes go through the property setters.
    private IReadOnlyList<AppThemeOption>          themeOptions          = [];
    private IReadOnlyList<InputProviderOption>     inputProviderOptions  = [];
    private IReadOnlyList<OutputProviderOption>    outputProviderOptions = [];
    private IReadOnlyList<ControllerStyleOption>   controllerStyleOptions = [];

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
    public string ThemeLabel                     => localizationService["ThemeLabel"];
    public string OpenSettingsButtonLabel        => localizationService["OpenSettingsButtonLabel"];
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
    public string DeleteProfileButtonLabel       => localizationService["DeleteProfileButton"];
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

            // Guard the OnProfileChanged handler so the Changed event
            // fired by SetCultureAsync (after its async settings save
            // completes) doesn't run the profile-sync chain — which has
            // been observed to flicker the language combo back to its
            // previous value during a culture change. The flag is held
            // until the async write completes; the OnProfileChanged
            // handler checks it before doing any profile sync work.
            isChangingCulture = true;
            localizationService.SetCulture(value.Code);
            var pending = profileSession.SetCultureAsync(value.Code);
            RefreshLocalizedText();

            _ = pending.ContinueWith(_ =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    isChangingCulture = false;

                    // Belt-and-braces: re-pin SelectedLanguage on the
                    // binding after the refresh chain has completed, so
                    // even if some other path attempted to push back
                    // the previous value the combo box re-syncs to what
                    // we actually picked.
                    OnPropertyChanged(nameof(SelectedLanguage));
                });
            }, TaskScheduler.Default);
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

            // Persist the theme choice to the active profile immediately so
            // a subsequent profile-changed event (e.g. raised by a culture
            // change via ProfileSession.SetCultureAsync) doesn't revert
            // the user's pick. Mirrors SelectedLanguage, which has the same
            // self-persist contract via the localization service.
            var profile = profileSession.CurrentProfile;
            var newKind = value.Kind.ToString();
            if (!string.Equals(profile.Ui.Theme, newKind, StringComparison.OrdinalIgnoreCase))
            {
                var updated = profile with { Ui = profile.Ui with { Theme = newKind } };
                _ = profileSession.SaveCurrentProfileAsync(updated);
            }
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
            OnPropertyChanged(nameof(IsEmulationActive));
            OnPropertyChanged(nameof(IsEmulationInactive));
        }
    }

    /// <summary>
    /// Background-colour preset for the controller panels. Each
    /// option carries both the human-readable label and the
    /// Avalonia-parseable brush string the panel actually applies.
    /// </summary>
    public sealed record PanelBackgroundOption(string Label, string BrushValue);

    /// <summary>
    /// Canonical presets exposed by the dashboard ComboBox. Chroma green
    /// and chroma blue are the standard "key colour" hex values used by
    /// OBS Studio and most chroma-key workflows; setting one of those
    /// lets streamers mask the controller panel onto a webcam feed
    /// without picking colours by hand.
    /// </summary>
    public IReadOnlyList<PanelBackgroundOption> PanelBackgroundOptions { get; } =
    [
        new("Dark (default)", "#09111B"),
        new("Chroma green",   "#00B140"),
        new("Chroma blue",    "#0047BB"),
        new("Pure black",     "#000000"),
        new("Transparent",    "Transparent"),
    ];

    /// <summary>
    /// The active preset. Setter writes the chosen brush into both
    /// controller VMs in unison and notifies the binding so the panels
    /// repaint. Persisted to
    /// <see cref="UiPreferences.ControllerPanelBackground"/> on the next
    /// Apply Dashboard Preferences.
    /// </summary>
    public PanelBackgroundOption? SelectedPanelBackgroundOption
    {
        get
        {
            // Match the current brush against the preset list. Custom
            // hex values that came in via the JSON file (no UI for them
            // yet) fall through to the default preset for display
            // purposes; the underlying brush is untouched.
            var current = PhysicalController.PanelBackgroundBrush;
            foreach (var opt in PanelBackgroundOptions)
            {
                if (string.Equals(opt.BrushValue, current, StringComparison.OrdinalIgnoreCase))
                {
                    return opt;
                }
            }
            return PanelBackgroundOptions.Count > 0 ? PanelBackgroundOptions[0] : null;
        }
        set
        {
            if (value is null) { return; }
            var target = value.BrushValue;
            if (string.Equals(PhysicalController.PanelBackgroundBrush, target,
                              StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            PhysicalController.PanelBackgroundBrush = target;
            VirtualController.PanelBackgroundBrush  = target;
            OnPropertyChanged(nameof(SelectedPanelBackgroundOption));
        }
    }

    /// <summary>
    /// True when the selected output provider actually emits a virtual
    /// device — i.e. anything other than the "preview" pseudo-sink and
    /// the "none" no-op. The dashboard's virtual-controller panel hides
    /// when this is false (design point 5: with no emulation there's
    /// no virtual device to visualise).
    /// </summary>
    public bool IsEmulationActive
    {
        get
        {
            var key = SelectedOutputProvider?.Key;
            return !string.IsNullOrWhiteSpace(key)
                && !string.Equals(key, "preview", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(key, "none",    StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Inverse of <see cref="IsEmulationActive"/>. Exists as its own
    /// property (rather than relying on a converter in AXAML) so the
    /// no-emulation single-panel layout can bind to a positive boolean.
    /// </summary>
    public bool IsEmulationInactive => !IsEmulationActive;

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
            DeleteProfileCommand.NotifyCanExecuteChanged();

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

        // When the user leaves the virtual panel on Auto, the visual style
        // would normally be inferred from the emitted device's name. That
        // misclassifies DS5 output as PS4: ViGEm Bus has no native DualSense
        // target, so the DS5 sink emits a DS4-shaped device, and a name-based
        // resolver sees "DualShock 4" / "Wireless Controller" and picks the
        // PS4 silhouette. Honour the user's choice of OUTPUT SINK instead —
        // the provider id (e.g. "vigem-ds5") is unambiguous.
        if (virtualStyle == ControllerVisualStyle.Auto)
        {
            virtualStyle = InferVirtualStyleFromProvider(snapshot.OutputProvider) ?? virtualStyle;
        }

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

            // Build the new Ui prefs by folding the pending theme-variant
            // picks (if any) and the current panel background into the
            // existing per-style fields. Picks that don't map to a known
            // style fall through unchanged.
            var ui = profileSession.CurrentProfile.Ui with
            {
                PhysicalControllerStyle = SelectedPhysicalStyle.Style,
                VirtualControllerStyle  = SelectedVirtualStyle.Style,
                Theme = themeKey,
                ControllerPanelBackground = PhysicalController.PanelBackgroundBrush,
            };

            ui = MergeVariantPick(ui, SelectedPhysicalStyle.Style, pendingPhysicalVariantPick);
            ui = MergeVariantPick(ui, SelectedVirtualStyle.Style,  pendingVirtualVariantPick);

            var profile = profileSession.CurrentProfile with
            {
                InputProvider = SelectedInputProvider.Key,
                OutputProvider = outputKey,
                PollingRateHz = (int)SelectedPollingRateHz,
                PreferredInputDeviceId = SelectedController?.Id ?? string.Empty,
                Ui = ui,
            };

            await profileSession.SaveCurrentProfileAsync(profile);

            // Picks are now persisted — clear the pending slots so a
            // later Apply that doesn't include another pick doesn't
            // re-write the same field.
            pendingPhysicalVariantPick = null;
            pendingVirtualVariantPick  = null;

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

    /// <summary>
    /// Deletes the currently selected profile. If the selected profile is the
    /// active one (the common case — the dropdown switches the active profile
    /// on selection), the runtime first switches to another profile, then
    /// deletes the original. Refuses to delete when there is no other profile
    /// to switch to (the user must always have at least one profile).
    /// </summary>
    /// <returns>A task that completes when deletion (and the subsequent UI
    /// refresh) finishes.</returns>
    private async Task DeleteProfileAsync()
    {
        var target = selectedProfileOption;
        if (target is null || string.IsNullOrWhiteSpace(target.Id))
        {
            return;
        }

        // Can't leave the user with zero profiles.
        if (AvailableProfiles.Count <= 1)
        {
            StatusText = localizationService["DeleteProfileBlockedLastOne"];
            return;
        }

        try
        {
            // If the user is deleting the active profile, switch to another
            // one first so ProfileSession.DeleteProfileAsync (which refuses
            // to delete the active profile) actually proceeds.
            if (string.Equals(target.Id, profileSession.CurrentProfile.Id, StringComparison.Ordinal))
            {
                var fallback = AvailableProfiles
                    .FirstOrDefault(p => !string.Equals(p.Id, target.Id, StringComparison.Ordinal));
                if (fallback is null)
                {
                    StatusText = localizationService["DeleteProfileBlockedLastOne"];
                    return;
                }

                await SwitchToProfileAsync(fallback.Id);
            }

            await profileSession.DeleteProfileAsync(target.Id);
            await RefreshAvailableProfilesAsync();

            // Snap the dropdown selection to whatever's now active.
            var current = AvailableProfiles
                .FirstOrDefault(p => p.Id == profileSession.CurrentProfile.Id)
                ?? AvailableProfiles.FirstOrDefault();
            if (current is not null && current != selectedProfileOption)
            {
                selectedProfileOption = current;
                OnPropertyChanged(nameof(SelectedProfileOption));
                OnPropertyChanged(nameof(SelectedProfileSummary));
            }

            DeleteProfileCommand.NotifyCanExecuteChanged();

            logger.LogInformation("Deleted profile {ProfileId} ({ProfileName}).", target.Id, target.Label);
            StatusText = string.Format(localizationService["ProfileDeletedStatus"], target.Label);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to delete profile {ProfileId}.", target.Id);
            StatusText = $"Delete failed: {exception.Message}";
        }
    }

    /// <summary>
    /// CanExecute predicate for <see cref="DeleteProfileCommand"/>. Disables
    /// the button when there's no selection or only one profile exists (the
    /// runtime always needs at least one profile).
    /// </summary>
    /// <returns>True when the currently selected profile may be deleted.</returns>
    private bool CanDeleteProfile()
    {
        return selectedProfileOption is not null
            && !string.IsNullOrWhiteSpace(selectedProfileOption.Id)
            && AvailableProfiles.Count > 1;
    }

    /// <summary>
    /// Opens the Options / Settings dialog. Resolves a fresh
    /// <see cref="SettingsDialogViewModel"/> (registered as transient
    /// in DI) and shows the dialog modally over the current main window.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="IServiceProvider"/> rather than direct DI of the
    /// view-model because the dialog is short-lived and we want a clean
    /// view-model state every open — preserves the cancel-discards-edits
    /// behaviour without having to manually reset everything.
    /// </remarks>
    private async Task OpenSettingsAsync()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
            desktop.MainWindow is null)
        {
            logger.LogWarning("OpenSettings invoked but no main window is available — ignoring.");
            return;
        }

        try
        {
            var dialogViewModel = serviceProvider.GetRequiredService<SettingsDialogViewModel>();
            var dialog = new SettingsDialog
            {
                DataContext = dialogViewModel,
            };

            logger.LogDebug("Opening settings dialog.");
            await dialog.ShowDialog(desktop.MainWindow);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Settings dialog failed to open.");
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

        // Push the persisted theme-variant ids into the controller VMs.
        // The VMs only consult these on the next RefreshActiveTheme(),
        // which happens automatically once a snapshot arrives. The
        // ResolveVisualStyle for each panel determines which field
        // applies: physical typically resolves via the detected device,
        // virtual uses the user-picked style directly.
        ApplyVariantPreferenceForStyle(PhysicalController, profile.Ui.PhysicalControllerStyle, profile.Ui);
        ApplyVariantPreferenceForStyle(VirtualController,  profile.Ui.VirtualControllerStyle,  profile.Ui);

        // Background brush — applies to both panels uniformly.
        PhysicalController.PanelBackgroundBrush = profile.Ui.ControllerPanelBackground;
        VirtualController.PanelBackgroundBrush  = profile.Ui.ControllerPanelBackground;
        OnPropertyChanged(nameof(SelectedPanelBackgroundOption));
    }

    /// <summary>
    /// Looks up the variant-id field on <paramref name="ui"/> that
    /// applies to <paramref name="style"/> and pushes it into
    /// <paramref name="vm"/>'s preference slot. Auto / None fall
    /// through with no preference — the registry's first variant wins.
    /// </summary>
    private static void ApplyVariantPreferenceForStyle(
        ControllerVisualStateViewModel vm,
        ControllerVisualStyle style,
        UiPreferences ui)
    {
        var preferredId = style switch
        {
            ControllerVisualStyle.PlayStation5 => ui.DualSenseVariantId,
            ControllerVisualStyle.PlayStation4 => ui.DualShock4VariantId,
            ControllerVisualStyle.PlayStation3 => ui.DualShock3VariantId,
            ControllerVisualStyle.Xbox         => ui.XboxVariantId,
            _ => string.Empty,
        };

        vm.SetPreferredVariantId(preferredId);
        // Trigger a refresh in case the style was already resolved.
        vm.RefreshActiveTheme();
    }

    /// <summary>
    /// Returns a copy of <paramref name="ui"/> with the variant id
    /// for <paramref name="style"/> replaced by <paramref name="pickedId"/>.
    /// No-op when <paramref name="pickedId"/> is null (nothing pending)
    /// or when the style is Auto/None (nothing to slot the pick into).
    /// </summary>
    private static UiPreferences MergeVariantPick(
        UiPreferences ui,
        ControllerVisualStyle style,
        string? pickedId)
    {
        if (string.IsNullOrEmpty(pickedId)) { return ui; }

        return style switch
        {
            ControllerVisualStyle.PlayStation5 => ui with { DualSenseVariantId  = pickedId },
            ControllerVisualStyle.PlayStation4 => ui with { DualShock4VariantId = pickedId },
            ControllerVisualStyle.PlayStation3 => ui with { DualShock3VariantId = pickedId },
            ControllerVisualStyle.Xbox         => ui with { XboxVariantId       = pickedId },
            _ => ui,
        };
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

        // CanExecute depends on AvailableProfiles.Count and on the selection,
        // both of which may have changed above.
        DeleteProfileCommand.NotifyCanExecuteChanged();
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
        _ = sb.AppendLine($"- {localizationService["DiagLogsLabel"]}: {AppPaths.LogsDirectory}");
        _ = sb.AppendLine($"- {localizationService["DiagLastInputFrameLabel"]}: {(snapshot.LastUpdated == default ? localizationService["DiagNoDataYetLabel"] : snapshot.LastUpdated.LocalDateTime.ToString("HH:mm:ss.fff"))}");
        _ = sb.AppendLine($"- {localizationService["DiagDashboardRefreshLabel"]}: " + string.Format(localizationService["DiagHzTargetFormat"], runtimeOptions.DashboardRefreshHz));
        _ = sb.AppendLine($"- {localizationService["DiagInputProviderRequestedLabel"]}: {profileSession.CurrentProfile.InputProvider}");
        _ = sb.AppendLine($"- {localizationService["DiagInputProviderEffectiveLabel"]}: {snapshot.InputProvider}");
        _ = sb.AppendLine($"- {localizationService["DiagOutputProviderLabel"]}: {snapshot.OutputProvider}");
        _ = sb.AppendLine($"- {localizationService["DiagViGEmEnabledLabel"]}: {runtimeOptions.EnableViGEm}");
        _ = sb.AppendLine($"- {localizationService["DiagControllersDetectedLabel"]}: {inputDeviceCatalog.Devices.Count}");
        _ = sb.AppendLine($"- {localizationService["DiagProviderStatusLabel"]}: {inputDeviceCatalog.ProviderStatus}");
        _ = sb.AppendLine($"- {localizationService["DiagExperimentalGameInputLabel"]}: {runtimeOptions.EnableExperimentalGameInput}");
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
            lines.Add($"- {localizationService["RuntimeNoteDemoActive"]}");
        }

        if (inputProvider.Equals("xinput", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"- {localizationService["RuntimeNoteXInputActive"]}");
        }

        if (IsSdlProvider(inputProvider))
        {
            lines.Add($"- {localizationService["RuntimeNoteSdlActive"]}");
        }

        if (inputProvider.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"- {localizationService["RuntimeNoteLiveInputDisabled"]}");
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

        // Re-build the option lists in the new culture and re-select
        // the currently-active option by its language-independent key.
        // Done before the OnPropertyChanged batch below so the combo
        // boxes pick up both the new ItemsSource and the new selection
        // in one pass.
        RebuildLocalizedOptionLists();

        // Push the culture change down into the per-controller panels
        // so their StyleLabel / MinimalSummaryText pick up the new
        // localized strings without waiting for a style change or the
        // next snapshot tick.
        PhysicalController.RefreshLocalizedLabels();
        VirtualController.RefreshLocalizedLabels();
        MappingEditor.RefreshLocalizedLabels();

        OnPropertyChanged(nameof(DashboardTabLabel));
        OnPropertyChanged(nameof(ProfilesTabLabel));
        OnPropertyChanged(nameof(DiagnosticsTabLabel));
        OnPropertyChanged(nameof(LanguageLabel));
        OnPropertyChanged(nameof(ThemeLabel));
        OnPropertyChanged(nameof(OpenSettingsButtonLabel));
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
        OnPropertyChanged(nameof(DeleteProfileButtonLabel));
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
        if (isSwitchingProfile || isChangingCulture)
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

    private IReadOnlyList<AppThemeOption> CreateThemeOptions()
    {
        return
        [
            new AppThemeOption(AppThemeKind.CyberBlue,      Localized("ThemeNameCyberBlue",      "Cyber Blue")),
            new AppThemeOption(AppThemeKind.MidnightPurple, Localized("ThemeNameMidnightPurple", "Midnight Purple")),
            new AppThemeOption(AppThemeKind.NeonGreen,      Localized("ThemeNameNeonGreen",      "Neon Green")),
            new AppThemeOption(AppThemeKind.SolarRed,       Localized("ThemeNameSolarRed",       "Solar Red")),
            new AppThemeOption(AppThemeKind.Light,          Localized("ThemeNameLight",          "Light")),
        ];
    }

    private IReadOnlyList<InputProviderOption> CreateInputProviderOptions()
    {
        return
        [
            new InputProviderOption("xinput",
                Localized("InputProviderXInputLabel",       "XInput"),
                Localized("InputProviderXInputDescription", "Windows native XInput driver. Enumerates up to 4 Xbox-compatible controllers.")),
            new InputProviderOption("sdl",
                Localized("InputProviderSdlLabel",          "SDL3 unified input"),
                Localized("InputProviderSdlDescription",    "Cross-platform SDL3 gamepad mapping plus joystick fallback.")),
            new InputProviderOption("openxinput",
                Localized("InputProviderOpenXInputLabel",       "OpenXInput"),
                Localized("InputProviderOpenXInputDescription", "Drop-in XInput replacement supporting more than 4 controllers. Scaffold — requires OpenXinput1_4.dll alongside the application.")),
            new InputProviderOption("x360ce",
                Localized("InputProviderX360ceLabel",       "x360ce"),
                Localized("InputProviderX360ceDescription", "DirectInput-to-XInput translator for legacy / non-XInput pads. Scaffold — requires x360ce runtime DLL.")),
            new InputProviderOption("ps3",
                Localized("InputProviderPs3Label",          "DualShock 3 (PS3)"),
                Localized("InputProviderPs3Description",    "Reads a paired DualShock 3 via DsHidMini or libusb. Scaffold — requires DsHidMini driver.")),
            new InputProviderOption("windows-midi",
                Localized("InputProviderWindowsMidiLabel",       "Windows MIDI input"),
                Localized("InputProviderWindowsMidiDescription", "Maps incoming MIDI events to virtual gamepad inputs. Scaffold — requires Windows MIDI Services and a profile-defined mapping.")),
            new InputProviderOption("demo",
                Localized("InputProviderDemoLabel",         "Demo preview"),
                Localized("InputProviderDemoDescription",   "Animated preview source for UI testing — no hardware required.")),
            new InputProviderOption("none",
                Localized("InputProviderNoneLabel",         "No live input"),
                Localized("InputProviderNoneDescription",   "Turns off live input and leaves the dashboard idle.")),
        ];
    }

    private IReadOnlyList<OutputProviderOption> CreateOutputProviderOptions()
    {
        return
        [
            new OutputProviderOption("vigem-xbox360",
                Localized("OutputProviderViGEmXbox360Label",       "ViGEm Xbox 360"),
                Localized("OutputProviderViGEmXbox360Description", "Virtual Xbox 360 controller via ViGEm Bus. Requires ViGEm Bus driver.")),
            new OutputProviderOption("vigem-ds4",
                Localized("OutputProviderViGEmDs4Label",           "ViGEm DualShock 4"),
                Localized("OutputProviderViGEmDs4Description",     "Virtual DualShock 4 controller via ViGEm Bus. Requires ViGEm Bus driver.")),
            new OutputProviderOption("vigem-ds5",
                Localized("OutputProviderViGEmDs5Label",           "ViGEm DualSense (DS5)"),
                Localized("OutputProviderViGEmDs5Description",     "Virtual DualSense controller via ViGEm Bus. Requires ViGEm Bus driver v1.22+.")),
            new OutputProviderOption("vjoy",
                Localized("OutputProviderVJoyLabel",               "vJoy virtual joystick"),
                Localized("OutputProviderVJoyDescription",         "Generic virtual joystick (up to 8 axes / 128 buttons). Scaffold — requires vJoy device driver.")),
            new OutputProviderOption("hidmaestro",
                Localized("OutputProviderHidMaestroLabel",         "HidMaestro virtual HID"),
                Localized("OutputProviderHidMaestroDescription",   "Custom HID device emulation with arbitrary report descriptors. Scaffold — requires HidMaestro driver.")),
            new OutputProviderOption("windows-midi-out",
                Localized("OutputProviderWindowsMidiOutLabel",       "Windows MIDI output"),
                Localized("OutputProviderWindowsMidiOutDescription", "Emits MIDI events from gamepad activity. Scaffold — requires Windows MIDI Services and a profile-defined mapping.")),
            new OutputProviderOption("preview",
                Localized("OutputProviderPreviewLabel",            "Preview only"),
                Localized("OutputProviderPreviewDescription",      "Shows the transformed output in the dashboard without creating a virtual device.")),
        ];
    }

    private IReadOnlyList<ControllerStyleOption> CreateControllerStyleOptions()
    {
        return
        [
            new ControllerStyleOption(ControllerVisualStyle.Auto,         Localized("ControllerStyleAuto",         "Auto")),
            new ControllerStyleOption(ControllerVisualStyle.Xbox,         Localized("ControllerStyleXbox",         "Xbox")),
            new ControllerStyleOption(ControllerVisualStyle.PlayStation4, Localized("ControllerStylePlayStation4", "PlayStation 4")),
            new ControllerStyleOption(ControllerVisualStyle.PlayStation5, Localized("ControllerStylePlayStation5", "PlayStation 5")),
            new ControllerStyleOption(ControllerVisualStyle.None,         Localized("ControllerStyleMinimal",      "Minimal")),
        ];
    }

    /// <summary>
    /// Looks up a localised string. If the key is missing from the
    /// active language's catalog (the localizer returns the key string
    /// itself for unknown keys), falls back to the supplied English
    /// default so labels still render correctly. Used by the option
    /// factories above.
    /// </summary>
    private string Localized(string key, string fallback)
    {
        var hit = localizationService[key];
        return string.IsNullOrEmpty(hit) || string.Equals(hit, key, StringComparison.Ordinal)
            ? fallback
            : hit;
    }

    /// <summary>
    /// Recreates every localised option list (themes, providers, styles)
    /// in the active culture and re-finds the currently-selected option
    /// by its stable key. Called from <see cref="RefreshLocalizedText"/>
    /// whenever the culture changes.
    /// </summary>
    /// <remarks>
    /// The option records are immutable, so flipping cultures means
    /// throwing the old collection away and binding to a new one. We
    /// match the old selection by the language-independent key
    /// (<see cref="AppThemeKind"/>, <see cref="ControllerVisualStyle"/>,
    /// or the provider id string) so the user keeps the same effective
    /// pick across the switch.
    /// </remarks>
    private void RebuildLocalizedOptionLists()
    {
        var previousThemeKind         = SelectedTheme?.Kind;
        var previousInputKey          = SelectedInputProvider?.Key;
        var previousOutputKey         = SelectedOutputProvider?.Key;
        var previousPhysicalStyle     = SelectedPhysicalStyle?.Style;
        var previousVirtualStyle      = SelectedVirtualStyle?.Style;

        ThemeOptions           = CreateThemeOptions();
        InputProviderOptions   = CreateInputProviderOptions();
        OutputProviderOptions  = CreateOutputProviderOptions();
        ControllerStyleOptions = CreateControllerStyleOptions();

        // Avoid running the full theme-persist side effects that fire
        // from the SelectedTheme setter — we're not changing the user's
        // theme, just relabelling the option. Set the backing field
        // directly and notify the binding pipeline manually.
        if (previousThemeKind is { } themeKind)
        {
            var match = ThemeOptions.FirstOrDefault(t => t.Kind == themeKind);
            if (match is not null && !ReferenceEquals(match, selectedTheme))
            {
                selectedTheme = match;
                OnPropertyChanged(nameof(SelectedTheme));
            }
        }

        if (previousInputKey is not null)
        {
            var match = InputProviderOptions.FirstOrDefault(o => o.Key == previousInputKey);
            if (match is not null)
            {
                SelectedInputProvider = match;
            }
        }

        if (previousOutputKey is not null)
        {
            var match = OutputProviderOptions.FirstOrDefault(o => o.Key == previousOutputKey);
            if (match is not null)
            {
                SelectedOutputProvider = match;
            }
        }

        if (previousPhysicalStyle is { } pStyle)
        {
            var match = ControllerStyleOptions.FirstOrDefault(o => o.Style == pStyle);
            if (match is not null)
            {
                SelectedPhysicalStyle = match;
            }
        }

        if (previousVirtualStyle is { } vStyle)
        {
            var match = ControllerStyleOptions.FirstOrDefault(o => o.Style == vStyle);
            if (match is not null)
            {
                SelectedVirtualStyle = match;
            }
        }
    }

    // ─── Misc helpers ─────────────────────────────────────────────────────────

    private string FormatScope(string scope)
    {
        return scope.Equals("virtual", StringComparison.OrdinalIgnoreCase)
            ? localizationService["VirtualOutputLabel"]
            : localizationService["PhysicalInputLabel"];
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

    /// <summary>
    /// Maps an output-provider identifier (e.g. <c>"vigem-ds5"</c>) to
    /// the visual style that best represents the controller layout the
    /// user expects to see. Returns <see langword="null"/> for providers
    /// whose layout depends on the connected device (e.g. raw passthrough
    /// or preview-only sinks) — the caller should fall through to the
    /// device-name-based resolver in that case.
    /// </summary>
    /// <remarks>
    /// Exists because ViGEm Bus has no native DualSense target: a
    /// <c>vigem-ds5</c> sink emits a DS4-shaped device on the bus, so
    /// inferring "DS5" from the device's actual name is impossible.
    /// The provider id, on the other hand, captures the user's intent
    /// unambiguously.
    /// </remarks>
    private static ControllerVisualStyle? InferVirtualStyleFromProvider(string? outputProvider)
    {
        if (string.IsNullOrWhiteSpace(outputProvider))
        {
            return null;
        }

        var n = outputProvider.Trim().ToLowerInvariant();
        return n switch
        {
            "vigem-xbox360" or "xbox360" or "x360" => ControllerVisualStyle.Xbox,
            "vigem-ds4" or "ds4" or "dualshock4" or "playstation4" or "ps4" => ControllerVisualStyle.PlayStation4,
            "vigem-ds5" or "ds5" or "dualsense" or "playstation5" or "ps5" => ControllerVisualStyle.PlayStation5,
            _ => null,
        };
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
