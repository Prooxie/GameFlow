using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Autofire.Core.Models;
using Autofire.Infrastructure.Localization;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Autofire.App.ViewModels;

/// <summary>
/// Manages the visual rule list and the add/edit form panel.
/// </summary>
public sealed class MappingEditorViewModel : ViewModelBase, IDisposable
{
    private readonly ILogger<MappingEditorViewModel> logger;
    private readonly ILocalizationService? localization;
    private string? preferredSelectionKey;
    private RuleKindOption? selectedNewRuleKind;
    private MappingRuleViewModel? editingRule;

    public MappingEditorViewModel(
        ILogger<MappingEditorViewModel> logger,
        ILocalizationService? localization = null)
    {
        this.logger = logger;
        this.localization = localization;

        RuleKindOptions = BuildRuleKindOptions();
        selectedNewRuleKind = RuleKindOptions.Count > 0 ? RuleKindOptions[0] : null;

        AddRuleCommand = new RelayCommand(AddRule);
        EditRuleCommand = new RelayCommand<MappingRuleViewModel>(EditRule);
        DeleteRuleCommand = new RelayCommand<MappingRuleViewModel>(DeleteRule);
        MoveUpCommand = new RelayCommand<MappingRuleViewModel>(MoveUp);
        MoveDownCommand = new RelayCommand<MappingRuleViewModel>(MoveDown);
        SaveEditCommand = new AsyncRelayCommand(SaveEditAsync);
        CancelEditCommand = new RelayCommand(CancelEdit);
    }

    /// <summary>
    /// Forces a culture-aware rebuild of every label exposed by this
    /// view-model (rule kind options + their descriptions, plus the
    /// AXAML-bound editor labels declared near the bottom of the file).
    /// Called by the hosting ShellViewModel from its culture-change
    /// refresh path.
    /// </summary>
    public void RefreshLocalizedLabels()
    {
        var previousKind = SelectedNewRuleKind?.Kind;
        RuleKindOptions = BuildRuleKindOptions();
        OnPropertyChanged(nameof(RuleKindOptions));

        if (previousKind is { } kind)
        {
            var match = RuleKindOptions.FirstOrDefault(o => o.Kind == kind);
            if (match is not null)
            {
                SelectedNewRuleKind = match;
            }
        }

        // Editor-form labels (defined further below).
        OnPropertyChanged(nameof(AddRuleButtonLabel));
        OnPropertyChanged(nameof(RuleNameLabel));
        OnPropertyChanged(nameof(RuleTypeLabel));
        OnPropertyChanged(nameof(RuleModeLabel));
        OnPropertyChanged(nameof(SourceButtonLabel));
        OnPropertyChanged(nameof(TargetButtonLabel));
        OnPropertyChanged(nameof(SuppressSourceButtonLabel));
        OnPropertyChanged(nameof(EnabledLabel));
        OnPropertyChanged(nameof(DeadzoneLabel));
        OnPropertyChanged(nameof(StoreScriptLabel));
        OnPropertyChanged(nameof(SelectRuleToEditLabel));
        OnPropertyChanged(nameof(OrUseAddRuleLabel));
        OnPropertyChanged(nameof(SaveButtonLabel));
        OnPropertyChanged(nameof(CancelButtonLabel));
        OnPropertyChanged(nameof(MappingRulesHeaderLabel));
    }

    // ─── Localised labels for AXAML bindings ──────────────────────────────────

    public string AddRuleButtonLabel          => Localized("MappingEditorAddRule",          "+ Add rule");
    public string RuleNameLabel               => Localized("MappingEditorRuleName",         "RULE NAME");
    public string RuleTypeLabel               => Localized("MappingEditorRuleType",         "RULE TYPE");
    public string RuleModeLabel               => Localized("MappingEditorMode",             "MODE");
    public string SourceButtonLabel           => Localized("MappingEditorSourceButton",     "SOURCE BUTTON");
    public string TargetButtonLabel           => Localized("MappingEditorTargetButton",     "TARGET BUTTON");
    public string SuppressSourceButtonLabel   => Localized("MappingEditorSuppressSource",   "Suppress source button");
    public string EnabledLabel                => Localized("MappingEditorEnabled",          "Enabled");
    public string DeadzoneLabel               => Localized("MappingEditorDeadzoneLabel",    "Deadzone and threshold");
    public string StoreScriptLabel            => Localized("MappingEditorStoreScript",      "Store a script for the selected control. Runtime execution is a planned step.");
    public string SelectRuleToEditLabel       => Localized("MappingEditorSelectRuleToEdit", "Select a rule to edit");
    public string OrUseAddRuleLabel           => Localized("MappingEditorOrUseAddRule",     "Or use 'Add rule' to create a new mapping.");
    public string SaveButtonLabel             => Localized("MappingEditorSaveButton",       "Save");
    public string CancelButtonLabel           => Localized("MappingEditorCancelButton",     "Cancel");
    public string MappingRulesHeaderLabel     => Localized("MappingEditorRulesHeader",      "MAPPING RULES");

    /// <summary>
    /// Internal helper: looks up <paramref name="key"/> via the optional
    /// localization service and falls back to <paramref name="fallback"/>
    /// if the service is null OR the key is missing.
    /// </summary>
    private string Localized(string key, string fallback)
    {
        if (localization is null) { return fallback; }
        var hit = localization[key];
        return string.IsNullOrEmpty(hit) || string.Equals(hit, key, StringComparison.Ordinal)
            ? fallback
            : hit;
    }

    public ObservableCollection<MappingRuleViewModel> Rules { get; } = [];

    public IReadOnlyList<RuleKindOption> RuleKindOptions { get; private set; }

    public RuleKindOption? SelectedNewRuleKind
    {
        get => selectedNewRuleKind;
        set => SetProperty(ref selectedNewRuleKind, value);
    }

    public MappingRuleViewModel? EditingRule
    {
        get => editingRule;
        private set
        {
            if (ReferenceEquals(editingRule, value))
            {
                return;
            }

            if (editingRule is not null)
            {
                editingRule.PropertyChanged -= OnEditingRulePropertyChanged;
            }

            if (SetProperty(ref editingRule, value) && editingRule is not null)
            {
                editingRule.PropertyChanged += OnEditingRulePropertyChanged;
            }

            OnPropertyChanged(nameof(EditPanelTitle));
        }
    }

    public bool IsEditing
    {
        get;
        private set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsNotEditing));
            }
        }
    }

    public bool IsNotEditing => !IsEditing;

    public bool IsAddingNew
    {
        get;
        private set => SetProperty(ref field, value);
    }

    public string EditPanelTitle => IsAddingNew ? "Add new rule" : $"Edit: {EditingRule?.Name ?? "rule"}";

    public ICommand AddRuleCommand { get; }
    public IRelayCommand<MappingRuleViewModel> EditRuleCommand { get; }
    public IRelayCommand<MappingRuleViewModel> DeleteRuleCommand { get; }
    public IRelayCommand<MappingRuleViewModel> MoveUpCommand { get; }
    public IRelayCommand<MappingRuleViewModel> MoveDownCommand { get; }
    public IAsyncRelayCommand SaveEditCommand { get; }
    public ICommand CancelEditCommand { get; }

    public event EventHandler<IReadOnlyList<MappingRule>>? RulesChanged;

    public void LoadFromProfile(ProfileDocument profile)
    {
        CancelEdit();
        preferredSelectionKey = null;

        foreach (var rule in Rules)
        {
            rule.PropertyChanged -= OnRulePropertyChanged;
        }

        Rules.Clear();

        foreach (var rule in profile.Rules)
        {
            var viewModel = MappingRuleViewModel.FromRule(rule);
            viewModel.PropertyChanged += OnRulePropertyChanged;
            Rules.Add(viewModel);
        }
    }

    public void SetPreferredSelection(string? selectionKey)
    {
        preferredSelectionKey = string.IsNullOrWhiteSpace(selectionKey)
            ? null
            : ControlRuleMatcher.EnsurePhysicalSelectionKey(selectionKey);
    }

    public void PreFillFromElement(string selectionKey)
    {
        preferredSelectionKey = ControlRuleMatcher.EnsurePhysicalSelectionKey(selectionKey);
        var suggestedKind = SuggestRuleKind(preferredSelectionKey);
        SelectedNewRuleKind = RuleKindOptions.FirstOrDefault(option => option.Kind == suggestedKind)
                              ?? (RuleKindOptions.Count > 0 ? RuleKindOptions[0] : null);
        EditingRule = CreatePreFilledRule(preferredSelectionKey, suggestedKind);
        IsEditing = true;
        IsAddingNew = true;
        OnPropertyChanged(nameof(EditPanelTitle));
    }

    private void AddRule()
    {
        var kind = SelectedNewRuleKind?.Kind ?? RuleKind.ButtonRemap;
        EditingRule = string.IsNullOrWhiteSpace(preferredSelectionKey)
            ? MappingRuleViewModel.CreateDefault(kind)
            : CreatePreFilledRule(preferredSelectionKey, kind);
        IsEditing = true;
        IsAddingNew = true;
        OnPropertyChanged(nameof(EditPanelTitle));
    }

    private void EditRule(MappingRuleViewModel? rule)
    {
        if (rule is null)
        {
            return;
        }

        EditingRule = rule.Clone();
        IsEditing = true;
        IsAddingNew = false;
        OnPropertyChanged(nameof(EditPanelTitle));
    }

    private void DeleteRule(MappingRuleViewModel? rule)
    {
        if (rule is null)
        {
            return;
        }

        rule.PropertyChanged -= OnRulePropertyChanged;
        _ = Rules.Remove(rule);

        if (EditingRule?.Id == rule.Id)
        {
            CancelEdit();
        }

        CommitRules();
    }

    private void MoveUp(MappingRuleViewModel? rule)
    {
        if (rule is null)
        {
            return;
        }

        var index = Rules.IndexOf(rule);
        if (index <= 0)
        {
            return;
        }

        Rules.Move(index, index - 1);
        CommitRules();
    }

    private void MoveDown(MappingRuleViewModel? rule)
    {
        if (rule is null)
        {
            return;
        }

        var index = Rules.IndexOf(rule);
        if (index < 0 || index >= Rules.Count - 1)
        {
            return;
        }

        Rules.Move(index, index + 1);
        CommitRules();
    }

    private Task SaveEditAsync()
    {
        if (EditingRule is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (IsAddingNew)
            {
                EditingRule.PropertyChanged += OnRulePropertyChanged;
                Rules.Add(EditingRule);
            }
            else
            {
                var existingIndex = -1;
                for (var index = 0; index < Rules.Count; index++)
                {
                    if (!string.Equals(Rules[index].Id, EditingRule.Id, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    existingIndex = index;
                    break;
                }

                if (existingIndex >= 0)
                {
                    Rules[existingIndex].PropertyChanged -= OnRulePropertyChanged;
                    EditingRule.PropertyChanged += OnRulePropertyChanged;
                    Rules[existingIndex] = EditingRule;
                }
            }

            CommitRules();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to save mapping rule.");
        }
        finally
        {
            CancelEdit();
        }

        return Task.CompletedTask;
    }

    private void CancelEdit()
    {
        EditingRule = null;
        IsEditing = false;
        IsAddingNew = false;
        OnPropertyChanged(nameof(EditPanelTitle));
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName == nameof(MappingRuleViewModel.Enabled))
        {
            CommitRules();
        }
    }

    private void OnEditingRulePropertyChanged(object? sender, PropertyChangedEventArgs eventArgs)
    {
        if (eventArgs.PropertyName is nameof(MappingRuleViewModel.Name) or nameof(MappingRuleViewModel.Kind))
        {
            OnPropertyChanged(nameof(EditPanelTitle));
        }
    }

    private void CommitRules()
    {
        var snapshot = Rules.Select(rule => rule.ToRule()).ToArray();
        RulesChanged?.Invoke(this, snapshot);
    }

    private static MappingRuleViewModel CreatePreFilledRule(string selectionKey, RuleKind kind)
    {
        var normalizedKey = ControlRuleMatcher.NormalizeSelectionKey(selectionKey);
        var viewModel = MappingRuleViewModel.CreateDefault(kind);

        if (ControlRuleMatcher.TryResolveButtonId(normalizedKey, out var button))
        {
            switch (kind)
            {
                case RuleKind.ButtonRemap:
                    viewModel.SourceButton = button;
                    viewModel.TargetButton = button;
                    viewModel.Name = $"Remap {ControlRuleMatcher.FormatButtonLabel(button)}";
                    break;

                case RuleKind.ButtonAutofire:
                    viewModel.SourceButton = button;
                    viewModel.TargetButton = button;
                    viewModel.Name = $"Turbo {ControlRuleMatcher.FormatButtonLabel(button)}";
                    break;

                case RuleKind.FreezeLastDirection:
                    viewModel.ActivationButton = button;
                    viewModel.Name = $"Freeze via {ControlRuleMatcher.FormatButtonLabel(button)}";
                    break;

                case RuleKind.Script:
                    viewModel.ControlKey = normalizedKey;
                    viewModel.Name = $"Script {ControlRuleMatcher.GetTitle(normalizedKey)}";
                    break;
            }

            return viewModel;
        }

        if (ControlRuleMatcher.TryResolveStickId(normalizedKey, out var stick))
        {
            switch (kind)
            {
                case RuleKind.StickThreshold:
                    viewModel.TargetStick = stick;
                    viewModel.Name = $"Threshold {ControlRuleMatcher.FormatStickLabel(stick)}";
                    break;

                case RuleKind.StickAutofire:
                    viewModel.SourceStick = stick;
                    viewModel.TargetStick = stick;
                    viewModel.Name = $"Autofire {ControlRuleMatcher.FormatStickLabel(stick)}";
                    break;

                case RuleKind.FreezeLastDirection:
                    viewModel.CaptureStick = stick;
                    viewModel.TargetStick = stick;
                    viewModel.Name = $"Freeze {ControlRuleMatcher.FormatStickLabel(stick)}";
                    break;

                case RuleKind.Script:
                    viewModel.ControlKey = normalizedKey;
                    viewModel.Name = $"Script {ControlRuleMatcher.GetTitle(normalizedKey)}";
                    break;
            }

            return viewModel;
        }

        if (kind == RuleKind.Script)
        {
            viewModel.ControlKey = normalizedKey;
            viewModel.Name = $"Script {ControlRuleMatcher.GetTitle(normalizedKey)}";
        }

        return viewModel;
    }

    private static RuleKind SuggestRuleKind(string selectionKey)
    {
        var normalizedKey = ControlRuleMatcher.NormalizeSelectionKey(selectionKey);

        return ControlRuleMatcher.TryResolveButtonId(normalizedKey, out _)
            ? RuleKind.ButtonRemap
            : ControlRuleMatcher.TryResolveStickId(normalizedKey, out _)
            ? RuleKind.StickThreshold
            : normalizedKey switch
            {
                "LeftTrigger.Analog" or "RightTrigger.Analog" => RuleKind.Script,
                _ => RuleKind.ButtonRemap
            };
    }

    private IReadOnlyList<RuleKindOption> BuildRuleKindOptions()
    {
        return
        [
            new RuleKindOption(RuleKind.ButtonRemap,
                Localized("RuleKindButtonRemapLabel",            "Button Remap"),
                Localized("RuleKindButtonRemapDescription",      "Route one button to another."),
                "#4F8CFF"),
            new RuleKindOption(RuleKind.ButtonAutofire,
                Localized("RuleKindButtonAutofireLabel",         "Button Autofire / Turbo"),
                Localized("RuleKindButtonAutofireDescription",   "Rapid-fire a button at a configurable rate."),
                "#F97316"),
            new RuleKindOption(RuleKind.StickThreshold,
                Localized("RuleKindStickThresholdLabel",         "Stick Threshold"),
                Localized("RuleKindStickThresholdDescription",   "Deadzone and threshold shaping for a stick."),
                "#10B981"),
            new RuleKindOption(RuleKind.StickAutofire,
                Localized("RuleKindStickAutofireLabel",          "Stick Autofire"),
                Localized("RuleKindStickAutofireDescription",    "Pulse one stick output from another stick."),
                "#A78BFA"),
            new RuleKindOption(RuleKind.FreezeLastDirection,
                Localized("RuleKindFreezeLastDirectionLabel",    "Freeze Last Direction"),
                Localized("RuleKindFreezeLastDirectionDescription", "Hold the last observed stick direction while a button is held."),
                "#00D4FF"),
            new RuleKindOption(RuleKind.Script,
                Localized("RuleKindScriptLabel",                 "Control Script"),
                Localized("RuleKindScriptDescription",           "Store script text for a specific control."),
                "#FACC15")
        ];
    }

    public void Dispose()
    {
        if (editingRule is not null)
        {
            editingRule.PropertyChanged -= OnEditingRulePropertyChanged;
        }

        foreach (var rule in Rules)
        {
            rule.PropertyChanged -= OnRulePropertyChanged;
        }
    }
}
