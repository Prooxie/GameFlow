using System.Globalization;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;

namespace GameFlow.App.ViewModels;

public sealed record ButtonIdOption(ButtonId Button, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}

public sealed record StickIdOption(StickId Stick, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}

public sealed record BlendModeOption(StickBlendMode Mode, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}

public sealed record RuleModeOption(RuleMode Mode, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}

public sealed record RuleKindOption(RuleKind Kind, string Label, string Description, string BadgeColor)
{
    public override string ToString()
    {
        return Label;
    }
}

public enum RuleKind
{
    ButtonRemap,
    ButtonAutofire,
    StickThreshold,
    StickAutofire,
    FreezeLastDirection,
    MultiButtonAutofire,
    RuleToggle,
    Script
}

/// <summary>
/// One row in a <see cref="RuleKind.MultiButtonAutofire"/> editor —
/// a target button plus its hold / release / delay timings. Mirrors
/// <see cref="GameFlow.Core.Models.Rules.MultiButtonAutofireStep"/>
/// but as a mutable, bindable view-model.
/// </summary>
public sealed class MultiButtonStepViewModel : ViewModelBase
{
    private ButtonId targetButton = ButtonId.South;
    private double holdMs = 50;
    private double releaseMs = 30;
    private double delayAfterMs;

    public IReadOnlyList<ButtonIdOption> ButtonOptions { get; } =
        [.. Enum.GetValues<ButtonId>().Select(b => new ButtonIdOption(b, ControlRuleMatcher.FormatButtonLabel(b)))];

    public ButtonId TargetButton
    {
        get => targetButton;
        set
        {
            if (targetButton == value) { return; }
            targetButton = value;
            OnPropertyChanged(nameof(TargetButton));
            OnPropertyChanged(nameof(SelectedTargetButtonOption));
        }
    }

    public ButtonIdOption? SelectedTargetButtonOption
    {
        get => ButtonOptions.FirstOrDefault(o => o.Button == TargetButton);
        set { if (value is not null) { TargetButton = value.Button; } }
    }

    public double HoldMs
    {
        get => holdMs;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 0, 5000);
            if (Math.Abs(holdMs - clamped) < 0.5) { return; }
            holdMs = clamped;
            OnPropertyChanged(nameof(HoldMs));
            OnPropertyChanged(nameof(HoldMsInput));
        }
    }

    public double ReleaseMs
    {
        get => releaseMs;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 0, 5000);
            if (Math.Abs(releaseMs - clamped) < 0.5) { return; }
            releaseMs = clamped;
            OnPropertyChanged(nameof(ReleaseMs));
            OnPropertyChanged(nameof(ReleaseMsInput));
        }
    }

    public double DelayAfterMs
    {
        get => delayAfterMs;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 0, 5000);
            if (Math.Abs(delayAfterMs - clamped) < 0.5) { return; }
            delayAfterMs = clamped;
            OnPropertyChanged(nameof(DelayAfterMs));
            OnPropertyChanged(nameof(DelayAfterMsInput));
        }
    }

    public string HoldMsInput
    {
        get => HoldMs.ToString("0", CultureInfo.InvariantCulture);
        set { if (TryParseDoubleShared(value, out var p)) { HoldMs = p; } }
    }

    public string ReleaseMsInput
    {
        get => ReleaseMs.ToString("0", CultureInfo.InvariantCulture);
        set { if (TryParseDoubleShared(value, out var p)) { ReleaseMs = p; } }
    }

    public string DelayAfterMsInput
    {
        get => DelayAfterMs.ToString("0", CultureInfo.InvariantCulture);
        set { if (TryParseDoubleShared(value, out var p)) { DelayAfterMs = p; } }
    }

    private static bool TryParseDoubleShared(string? value, out double parsed)
    {
        var normalized = value?.Trim().Replace(',', '.') ?? string.Empty;
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }
}

/// <summary>
/// One selectable sibling rule in a <see cref="RuleKind.RuleToggle"/>
/// editor. <see cref="IsSelected"/> mirrors membership in the rule's
/// TargetRuleIds list.
/// </summary>
public sealed class RuleToggleTargetOption : ViewModelBase
{
    private bool isSelected;

    public RuleToggleTargetOption(string ruleId, string ruleName)
    {
        RuleId = ruleId;
        RuleName = ruleName;
    }

    public string RuleId { get; }
    public string RuleName { get; }

    public bool IsSelected
    {
        get => isSelected;
        set => SetProperty(ref isSelected, value);
    }
}

public sealed class MappingRuleViewModel : ViewModelBase
{
    private static readonly IReadOnlyList<ButtonIdOption> ButtonOptionsSource = BuildButtonOptions();
    private static readonly IReadOnlyList<StickIdOption> StickOptionsSource = BuildStickOptions();
    private static readonly IReadOnlyList<BlendModeOption> BlendModeOptionsSource = BuildBlendOptions();
    private static readonly IReadOnlyList<RuleModeOption> ModeOptionsSource = BuildModeOptions();
    private static readonly IReadOnlyList<RuleKindOption> KindOptionsSource = BuildRuleKindOptions();

    private RuleKind kind = RuleKind.ButtonRemap;
    private string name = "New rule";
    private bool enabled = true;
    private RuleMode mode = RuleMode.Modify;
    private ButtonId sourceButton = ButtonId.South;
    private ButtonId targetButton = ButtonId.South;
    private bool suppressSourceButton;
    private StickId sourceStick = StickId.Right;
    private StickId targetStick = StickId.Left;
    private StickId captureStick = StickId.Right;
    private StickBlendMode blendMode = StickBlendMode.Additive;
    private bool suppressSourceStick;
    private bool suppressThresholdSourceStick;
    private bool suppressActivationButton;
    private bool suppressCaptureStick;
    private double deadzone = 0.12;
    private double fullAt = 0.90;
    private double holdMs = 128;
    private double releaseMs = 32;
    private ButtonId activationButton = ButtonId.LeftShoulder;
    private bool pulseEnabled = true;
    private string controlKey = string.Empty;
    private string scriptCode = string.Empty;
    private bool suppressScriptSourceInput;

    /// <summary>Editable timeline for a MultiButtonAutofire rule.</summary>
    public System.Collections.ObjectModel.ObservableCollection<MultiButtonStepViewModel> Steps { get; } = [];

    /// <summary>Selectable sibling rules for a RuleToggle rule.</summary>
    public System.Collections.ObjectModel.ObservableCollection<RuleToggleTargetOption> ToggleTargets { get; } = [];

    public MappingRuleViewModel()
    {
        AddStepCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(() =>
        {
            Steps.Add(new MultiButtonStepViewModel());
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(HasSteps));
        });
        RemoveStepCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<MultiButtonStepViewModel>(step =>
        {
            if (step is not null)
            {
                _ = Steps.Remove(step);
                OnPropertyChanged(nameof(SummaryText));
                OnPropertyChanged(nameof(HasSteps));
            }
        });
    }

    /// <summary>Adds a new empty step to a MultiButtonAutofire rule.</summary>
    public System.Windows.Input.ICommand AddStepCommand { get; }

    /// <summary>Removes the passed step from a MultiButtonAutofire rule.</summary>
    public System.Windows.Input.ICommand RemoveStepCommand { get; }

    public bool HasSteps => Steps.Count > 0;

    /// <summary>
    /// Ids stashed by <see cref="FromRule"/> for a RuleToggle rule
    /// before sibling-rule names are known. The editor VM consumes
    /// these in <see cref="PopulateToggleTargets"/>.
    /// </summary>
    public IReadOnlyList<string> PendingToggleTargetIds { get; set; } = [];

    /// <summary>
    /// Rebuilds <see cref="ToggleTargets"/> from the supplied sibling
    /// rules (id + display name), pre-selecting any whose id is in the
    /// rule's current target set (either the live ToggleTargets
    /// selection or the pending ids loaded from disk). The rule's own
    /// id is excluded so a toggle can't target itself.
    /// </summary>
    public void PopulateToggleTargets(IEnumerable<(string Id, string Name)> siblings)
    {
        var alreadySelected = new HashSet<string>(
            ToggleTargets.Where(t => t.IsSelected).Select(t => t.RuleId),
            StringComparer.Ordinal);
        foreach (var id in PendingToggleTargetIds)
        {
            _ = alreadySelected.Add(id);
        }

        ToggleTargets.Clear();
        foreach (var (id, name) in siblings)
        {
            if (string.Equals(id, Id, StringComparison.Ordinal)) { continue; }
            ToggleTargets.Add(new RuleToggleTargetOption(id, name)
            {
                IsSelected = alreadySelected.Contains(id)
            });
        }
        OnPropertyChanged(nameof(HasToggleTargets));
    }

    public bool HasToggleTargets => ToggleTargets.Count > 0;

    public string Id { get; private set; } = Guid.NewGuid().ToString("N");

    public IReadOnlyList<ButtonIdOption> ButtonOptions => ButtonOptionsSource;
    public IReadOnlyList<StickIdOption> StickOptions => StickOptionsSource;
    public IReadOnlyList<BlendModeOption> BlendModeOptions => BlendModeOptionsSource;
    public IReadOnlyList<RuleModeOption> ModeOptions => ModeOptionsSource;
    public IReadOnlyList<RuleKindOption> KindOptions => KindOptionsSource;

    public RuleKind Kind
    {
        get => kind;
        set
        {
            if (kind == value)
            {
                return;
            }

            kind = value;
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(SelectedKindOption));
            OnPropertyChanged(nameof(IsButtonRemap));
            OnPropertyChanged(nameof(IsButtonAutofire));
            OnPropertyChanged(nameof(IsStickThreshold));
            OnPropertyChanged(nameof(IsStickAutofire));
            OnPropertyChanged(nameof(IsFreeze));
            OnPropertyChanged(nameof(IsMultiButtonAutofire));
            OnPropertyChanged(nameof(IsRuleToggle));
            OnPropertyChanged(nameof(IsScriptRule));
            OnPropertyChanged(nameof(HasTiming));
            OnPropertyChanged(nameof(ShowPulseTiming));
            OnPropertyChanged(nameof(KindLabel));
            OnPropertyChanged(nameof(KindBadgeColor));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string Name
    {
        get => name;
        set => SetProperty(ref name, value);
    }

    public bool Enabled
    {
        get => enabled;
        set => SetProperty(ref enabled, value);
    }

    public RuleMode Mode
    {
        get => mode;
        set
        {
            if (mode == value)
            {
                return;
            }

            mode = value;
            OnPropertyChanged(nameof(Mode));
            OnPropertyChanged(nameof(SelectedModeOption));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public ButtonId SourceButton
    {
        get => sourceButton;
        set
        {
            if (sourceButton == value)
            {
                return;
            }

            sourceButton = value;
            OnPropertyChanged(nameof(SourceButton));
            OnPropertyChanged(nameof(SelectedSourceButtonOption));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public ButtonId TargetButton
    {
        get => targetButton;
        set
        {
            if (targetButton == value)
            {
                return;
            }

            targetButton = value;
            OnPropertyChanged(nameof(TargetButton));
            OnPropertyChanged(nameof(SelectedTargetButtonOption));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public bool SuppressSourceButton
    {
        get => suppressSourceButton;
        set
        {
            if (SetProperty(ref suppressSourceButton, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public StickId SourceStick
    {
        get => sourceStick;
        set
        {
            if (sourceStick == value)
            {
                return;
            }

            sourceStick = value;
            OnPropertyChanged(nameof(SourceStick));
            OnPropertyChanged(nameof(SelectedSourceStickOption));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public StickId TargetStick
    {
        get => targetStick;
        set
        {
            if (targetStick == value)
            {
                return;
            }

            targetStick = value;
            OnPropertyChanged(nameof(TargetStick));
            OnPropertyChanged(nameof(SelectedTargetStickOption));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public StickId CaptureStick
    {
        get => captureStick;
        set
        {
            if (captureStick == value)
            {
                return;
            }

            captureStick = value;
            OnPropertyChanged(nameof(CaptureStick));
            OnPropertyChanged(nameof(SelectedCaptureStickOption));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public StickBlendMode BlendMode
    {
        get => blendMode;
        set
        {
            if (blendMode == value)
            {
                return;
            }

            blendMode = value;
            OnPropertyChanged(nameof(BlendMode));
            OnPropertyChanged(nameof(SelectedBlendModeOption));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public bool SuppressSourceStick
    {
        get => suppressSourceStick;
        set
        {
            if (SetProperty(ref suppressSourceStick, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public bool SuppressThresholdSourceStick
    {
        get => suppressThresholdSourceStick;
        set
        {
            if (SetProperty(ref suppressThresholdSourceStick, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public bool SuppressActivationButton
    {
        get => suppressActivationButton;
        set
        {
            if (SetProperty(ref suppressActivationButton, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public bool SuppressCaptureStick
    {
        get => suppressCaptureStick;
        set
        {
            if (SetProperty(ref suppressCaptureStick, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public double Deadzone
    {
        get => deadzone;
        set
        {
            if (Math.Abs(deadzone - value) < 0.001)
            {
                return;
            }

            deadzone = Math.Clamp(value, 0.0, 0.50);
            OnPropertyChanged(nameof(Deadzone));
            OnPropertyChanged(nameof(DeadzoneText));
            OnPropertyChanged(nameof(DeadzoneInput));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public double FullAt
    {
        get => fullAt;
        set
        {
            if (Math.Abs(fullAt - value) < 0.001)
            {
                return;
            }

            fullAt = Math.Clamp(value, 0.50, 1.0);
            OnPropertyChanged(nameof(FullAt));
            OnPropertyChanged(nameof(FullAtText));
            OnPropertyChanged(nameof(FullAtInput));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public double HoldMs
    {
        get => holdMs;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 8, 2000);
            if (Math.Abs(holdMs - clamped) < 0.5)
            {
                return;
            }

            holdMs = clamped;
            OnPropertyChanged(nameof(HoldMs));
            OnPropertyChanged(nameof(HoldMsText));
            OnPropertyChanged(nameof(HoldMsInput));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public double ReleaseMs
    {
        get => releaseMs;
        set
        {
            var clamped = Math.Clamp(Math.Round(value), 4, 1000);
            if (Math.Abs(releaseMs - clamped) < 0.5)
            {
                return;
            }

            releaseMs = clamped;
            OnPropertyChanged(nameof(ReleaseMs));
            OnPropertyChanged(nameof(ReleaseMsText));
            OnPropertyChanged(nameof(ReleaseMsInput));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public ButtonId ActivationButton
    {
        get => activationButton;
        set
        {
            if (activationButton == value)
            {
                return;
            }

            activationButton = value;
            OnPropertyChanged(nameof(ActivationButton));
            OnPropertyChanged(nameof(SelectedActivationButtonOption));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public bool PulseEnabled
    {
        get => pulseEnabled;
        set
        {
            if (pulseEnabled == value)
            {
                return;
            }

            pulseEnabled = value;
            OnPropertyChanged(nameof(PulseEnabled));
            OnPropertyChanged(nameof(ShowPulseTiming));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string ControlKey
    {
        get => controlKey;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(controlKey, normalized, StringComparison.Ordinal))
            {
                return;
            }

            controlKey = normalized;
            OnPropertyChanged(nameof(ControlKey));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public string ScriptCode
    {
        get => scriptCode;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(scriptCode, normalized, StringComparison.Ordinal))
            {
                return;
            }

            scriptCode = normalized;
            OnPropertyChanged(nameof(ScriptCode));
            OnPropertyChanged(nameof(ScriptPreviewText));
            OnPropertyChanged(nameof(SummaryText));
        }
    }

    public bool SuppressScriptSourceInput
    {
        get => suppressScriptSourceInput;
        set
        {
            if (SetProperty(ref suppressScriptSourceInput, value))
            {
                OnPropertyChanged(nameof(SummaryText));
            }
        }
    }

    public bool IsButtonRemap => Kind == RuleKind.ButtonRemap;
    public bool IsButtonAutofire => Kind == RuleKind.ButtonAutofire;
    public bool IsStickThreshold => Kind == RuleKind.StickThreshold;
    public bool IsStickAutofire => Kind == RuleKind.StickAutofire;
    public bool IsFreeze => Kind == RuleKind.FreezeLastDirection;
    public bool IsMultiButtonAutofire => Kind == RuleKind.MultiButtonAutofire;
    public bool IsRuleToggle => Kind == RuleKind.RuleToggle;
    public bool IsScriptRule => Kind == RuleKind.Script;
    public bool HasTiming => Kind is RuleKind.ButtonAutofire or RuleKind.StickAutofire or RuleKind.FreezeLastDirection;
    public bool ShowPulseTiming => HasTiming && (Kind != RuleKind.FreezeLastDirection || PulseEnabled);

    public string DeadzoneText => Deadzone.ToString("0.00", CultureInfo.InvariantCulture);
    public string FullAtText => FullAt.ToString("0.00", CultureInfo.InvariantCulture);
    public string HoldMsText => $"{HoldMs:0} ms";
    public string ReleaseMsText => $"{ReleaseMs:0} ms";
    public string ScriptPreviewText => FormatScriptPreview(ScriptCode);

    public string DeadzoneInput
    {
        get => Deadzone.ToString("0.00", CultureInfo.InvariantCulture);
        set
        {
            if (TryParseDouble(value, out var parsed))
            {
                Deadzone = parsed;
            }
        }
    }

    public string FullAtInput
    {
        get => FullAt.ToString("0.00", CultureInfo.InvariantCulture);
        set
        {
            if (TryParseDouble(value, out var parsed))
            {
                FullAt = parsed;
            }
        }
    }

    public string HoldMsInput
    {
        get => HoldMs.ToString("0", CultureInfo.InvariantCulture);
        set
        {
            if (TryParseDouble(value, out var parsed))
            {
                HoldMs = parsed;
            }
        }
    }

    public string ReleaseMsInput
    {
        get => ReleaseMs.ToString("0", CultureInfo.InvariantCulture);
        set
        {
            if (TryParseDouble(value, out var parsed))
            {
                ReleaseMs = parsed;
            }
        }
    }

    public string KindLabel => Kind switch
    {
        RuleKind.ButtonRemap => "Button Remap",
        RuleKind.ButtonAutofire => "Button Autofire / Turbo",
        RuleKind.StickThreshold => "Stick Threshold",
        RuleKind.StickAutofire => "Stick Autofire",
        RuleKind.FreezeLastDirection => "Freeze Last Direction",
        RuleKind.MultiButtonAutofire => "Multi-Button Autofire",
        RuleKind.RuleToggle => "Rule Toggle",
        RuleKind.Script => "Control Script",
        _ => "Rule"
    };

    public string KindBadgeColor => Kind switch
    {
        RuleKind.ButtonRemap => "#4F8CFF",
        RuleKind.ButtonAutofire => "#F97316",
        RuleKind.StickThreshold => "#10B981",
        RuleKind.StickAutofire => "#A78BFA",
        RuleKind.FreezeLastDirection => "#00D4FF",
        RuleKind.MultiButtonAutofire => "#FB7185",
        RuleKind.RuleToggle => "#34D399",
        RuleKind.Script => "#FACC15",
        _ => "#64748B"
    };

    public string SummaryText => Kind switch
    {
        RuleKind.ButtonRemap =>
            $"{ControlRuleMatcher.FormatButtonLabel(SourceButton)} → {ControlRuleMatcher.FormatButtonLabel(TargetButton)} · {FormatMode(Mode)}{FormatSuffix(SuppressSourceButton, "suppress src")}",
        RuleKind.ButtonAutofire =>
            $"{ControlRuleMatcher.FormatButtonLabel(SourceButton)} → {ControlRuleMatcher.FormatButtonLabel(TargetButton)} · {HoldMs:0}/{ReleaseMs:0} ms · {FormatMode(Mode)}{FormatSuffix(SuppressSourceButton, "suppress src")}",
        RuleKind.StickThreshold =>
            $"{ControlRuleMatcher.FormatStickLabel(TargetStick)} · deadzone {Deadzone:0.00} · full {FullAt:0.00} · {FormatMode(Mode)}{FormatSuffix(SuppressThresholdSourceStick, "suppress raw")}",
        RuleKind.StickAutofire =>
            $"{ControlRuleMatcher.FormatStickLabel(SourceStick)} → {ControlRuleMatcher.FormatStickLabel(TargetStick)} · dz {Deadzone:0.00} · full {FullAt:0.00} · {HoldMs:0}/{ReleaseMs:0} ms · {FormatMode(Mode)}{FormatSuffix(SuppressSourceStick, "suppress src")}",
        RuleKind.FreezeLastDirection =>
            $"{ControlRuleMatcher.FormatButtonLabel(ActivationButton)} → freeze {ControlRuleMatcher.FormatStickLabel(CaptureStick)} into {ControlRuleMatcher.FormatStickLabel(TargetStick)} · {FormatMode(Mode)}{FormatSuffix(SuppressActivationButton, "suppress btn")}{FormatSuffix(SuppressCaptureStick, "suppress stick")}",
        RuleKind.MultiButtonAutofire =>
            $"{ControlRuleMatcher.FormatButtonLabel(SourceButton)} → loop {Steps.Count} step(s) · {FormatMode(Mode)}{FormatSuffix(SuppressSourceButton, "suppress src")}",
        RuleKind.RuleToggle =>
            $"{ControlRuleMatcher.FormatButtonLabel(SourceButton)} → toggle {ToggleTargets.Count(t => t.IsSelected)} rule(s){FormatSuffix(SuppressSourceButton, "suppress src")}",
        RuleKind.Script =>
            $"{(string.IsNullOrWhiteSpace(ControlKey) ? "No control selected" : ControlRuleMatcher.GetTitle(ControlKey))} · script{FormatSuffix(SuppressScriptSourceInput, "suppress src")}",
        _ => string.Empty
    };

    public RuleKindOption? SelectedKindOption
    {
        get => KindOptions.FirstOrDefault(option => option.Kind == Kind);
        set
        {
            if (value is not null)
            {
                Kind = value.Kind;
            }
        }
    }

    public ButtonIdOption? SelectedSourceButtonOption
    {
        get => ButtonOptions.FirstOrDefault(option => option.Button == SourceButton);
        set
        {
            if (value is not null)
            {
                SourceButton = value.Button;
            }
        }
    }

    public ButtonIdOption? SelectedTargetButtonOption
    {
        get => ButtonOptions.FirstOrDefault(option => option.Button == TargetButton);
        set
        {
            if (value is not null)
            {
                TargetButton = value.Button;
            }
        }
    }

    public ButtonIdOption? SelectedActivationButtonOption
    {
        get => ButtonOptions.FirstOrDefault(option => option.Button == ActivationButton);
        set
        {
            if (value is not null)
            {
                ActivationButton = value.Button;
            }
        }
    }

    public StickIdOption? SelectedSourceStickOption
    {
        get => StickOptions.FirstOrDefault(option => option.Stick == SourceStick);
        set
        {
            if (value is not null)
            {
                SourceStick = value.Stick;
            }
        }
    }

    public StickIdOption? SelectedTargetStickOption
    {
        get => StickOptions.FirstOrDefault(option => option.Stick == TargetStick);
        set
        {
            if (value is not null)
            {
                TargetStick = value.Stick;
            }
        }
    }

    public StickIdOption? SelectedCaptureStickOption
    {
        get => StickOptions.FirstOrDefault(option => option.Stick == CaptureStick);
        set
        {
            if (value is not null)
            {
                CaptureStick = value.Stick;
            }
        }
    }

    public BlendModeOption? SelectedBlendModeOption
    {
        get => BlendModeOptions.FirstOrDefault(option => option.Mode == BlendMode);
        set
        {
            if (value is not null)
            {
                BlendMode = value.Mode;
            }
        }
    }

    public RuleModeOption? SelectedModeOption
    {
        get => ModeOptions.FirstOrDefault(option => option.Mode == Mode);
        set
        {
            if (value is not null)
            {
                Mode = value.Mode;
            }
        }
    }

    public MappingRule ToRule()
    {
        return Kind switch
        {
            RuleKind.ButtonRemap => new ButtonRemapRule
            {
                Id = Id,
                Name = Name,
                Enabled = Enabled,
                Mode = Mode,
                SourceButton = SourceButton,
                TargetButton = TargetButton,
                SuppressSourceButton = SuppressSourceButton
            },
            RuleKind.ButtonAutofire => new ButtonAutofireRule
            {
                Id = Id,
                Name = Name,
                Enabled = Enabled,
                Mode = Mode,
                SourceButton = SourceButton,
                TargetButton = TargetButton,
                SuppressSourceButton = SuppressSourceButton,
                Timing = new PulseTimingOptions
                {
                    HoldMs = (int)HoldMs,
                    ReleaseMs = (int)ReleaseMs
                }
            },
            RuleKind.StickThreshold => new StickThresholdRule
            {
                Id = Id,
                Name = Name,
                Enabled = Enabled,
                Mode = Mode,
                TargetStick = TargetStick,
                Deadzone = (float)Deadzone,
                FullAt = (float)FullAt,
                SuppressSourceStick = SuppressThresholdSourceStick
            },
            RuleKind.StickAutofire => new StickAutofireRule
            {
                Id = Id,
                Name = Name,
                Enabled = Enabled,
                Mode = Mode,
                SourceStick = SourceStick,
                TargetStick = TargetStick,
                BlendMode = BlendMode,
                SuppressSourceStick = SuppressSourceStick,
                ActivationDeadzone = (float)Deadzone,
                ActivationFullAt = (float)FullAt,
                Timing = new PulseTimingOptions
                {
                    HoldMs = (int)HoldMs,
                    ReleaseMs = (int)ReleaseMs
                }
            },
            RuleKind.FreezeLastDirection => new FreezeLastDirectionRule
            {
                Id = Id,
                Name = Name,
                Enabled = Enabled,
                Mode = Mode,
                ActivationButton = ActivationButton,
                CaptureStick = CaptureStick,
                TargetStick = TargetStick,
                BlendMode = BlendMode,
                SuppressActivationButton = SuppressActivationButton,
                SuppressCaptureStick = SuppressCaptureStick,
                PulseEnabled = PulseEnabled,
                Timing = new PulseTimingOptions
                {
                    HoldMs = (int)HoldMs,
                    ReleaseMs = (int)ReleaseMs
                }
            },
            RuleKind.MultiButtonAutofire => new MultiButtonAutofireRule
            {
                Id = Id,
                Name = Name,
                Enabled = Enabled,
                Mode = Mode,
                SourceButton = SourceButton,
                SuppressSourceButton = SuppressSourceButton,
                Steps = [.. Steps.Select(s => new MultiButtonAutofireStep
                {
                    TargetButton = s.TargetButton,
                    HoldMs = (int)s.HoldMs,
                    ReleaseMs = (int)s.ReleaseMs,
                    DelayAfterMs = (int)s.DelayAfterMs
                })]
            },
            RuleKind.RuleToggle => new RuleToggleRule
            {
                Id = Id,
                Name = Name,
                Enabled = Enabled,
                Mode = Mode,
                SourceButton = SourceButton,
                SuppressSourceButton = SuppressSourceButton,
                // When the editor has populated ToggleTargets, the live
                // checkbox selection is authoritative. Otherwise (rule
                // is sitting in the list, never opened for edit) fall
                // back to the ids loaded from disk so Clone()/CommitRules
                // don't silently drop the targets.
                TargetRuleIds = ToggleTargets.Count > 0
                    ? [.. ToggleTargets.Where(t => t.IsSelected).Select(t => t.RuleId)]
                    : [.. PendingToggleTargetIds]
            },
            RuleKind.Script => new ControlScriptRule
            {
                Id = Id,
                Name = Name,
                Enabled = Enabled,
                Mode = Mode,
                ControlKey = ControlRuleMatcher.NormalizeSelectionKey(ControlKey),
                ScriptCode = ScriptCode,
                SuppressSourceInput = SuppressScriptSourceInput
            },
            _ => throw new InvalidOperationException($"Unknown rule kind: {Kind}")
        };
    }

    public MappingRuleViewModel Clone()
    {
        return FromRule(ToRule());
    }

    public static MappingRuleViewModel FromRule(MappingRule rule)
    {
        var viewModel = new MappingRuleViewModel
        {
            Id = rule.Id,
            name = rule.Name,
            enabled = rule.Enabled,
            mode = rule.Mode
        };

        switch (rule)
        {
            case ButtonRemapRule typedRule:
                viewModel.kind = RuleKind.ButtonRemap;
                viewModel.sourceButton = typedRule.SourceButton;
                viewModel.targetButton = typedRule.TargetButton;
                viewModel.suppressSourceButton = typedRule.SuppressSourceButton;
                break;

            case ButtonAutofireRule typedRule:
                viewModel.kind = RuleKind.ButtonAutofire;
                viewModel.sourceButton = typedRule.SourceButton;
                viewModel.targetButton = typedRule.TargetButton;
                viewModel.suppressSourceButton = typedRule.SuppressSourceButton;
                viewModel.holdMs = typedRule.Timing.HoldMs;
                viewModel.releaseMs = typedRule.Timing.ReleaseMs;
                break;

            case StickThresholdRule typedRule:
                viewModel.kind = RuleKind.StickThreshold;
                viewModel.targetStick = typedRule.TargetStick;
                viewModel.deadzone = typedRule.Deadzone;
                viewModel.fullAt = typedRule.FullAt;
                viewModel.suppressThresholdSourceStick = typedRule.SuppressSourceStick;
                break;

            case StickAutofireRule typedRule:
                viewModel.kind = RuleKind.StickAutofire;
                viewModel.sourceStick = typedRule.SourceStick;
                viewModel.targetStick = typedRule.TargetStick;
                viewModel.blendMode = typedRule.BlendMode;
                viewModel.suppressSourceStick = typedRule.SuppressSourceStick;
                viewModel.deadzone = typedRule.ActivationDeadzone;
                viewModel.fullAt = typedRule.ActivationFullAt;
                viewModel.holdMs = typedRule.Timing.HoldMs;
                viewModel.releaseMs = typedRule.Timing.ReleaseMs;
                break;

            case FreezeLastDirectionRule typedRule:
                viewModel.kind = RuleKind.FreezeLastDirection;
                viewModel.activationButton = typedRule.ActivationButton;
                viewModel.captureStick = typedRule.CaptureStick;
                viewModel.targetStick = typedRule.TargetStick;
                viewModel.blendMode = typedRule.BlendMode;
                viewModel.suppressActivationButton = typedRule.SuppressActivationButton;
                viewModel.suppressCaptureStick = typedRule.SuppressCaptureStick;
                viewModel.pulseEnabled = typedRule.PulseEnabled;
                viewModel.holdMs = typedRule.Timing.HoldMs;
                viewModel.releaseMs = typedRule.Timing.ReleaseMs;
                break;

            case MultiButtonAutofireRule typedRule:
                viewModel.kind = RuleKind.MultiButtonAutofire;
                viewModel.sourceButton = typedRule.SourceButton;
                viewModel.suppressSourceButton = typedRule.SuppressSourceButton;
                foreach (var step in typedRule.Steps)
                {
                    viewModel.Steps.Add(new MultiButtonStepViewModel
                    {
                        TargetButton = step.TargetButton,
                        HoldMs = step.HoldMs,
                        ReleaseMs = step.ReleaseMs,
                        DelayAfterMs = step.DelayAfterMs
                    });
                }
                break;

            case RuleToggleRule typedRule:
                viewModel.kind = RuleKind.RuleToggle;
                viewModel.sourceButton = typedRule.SourceButton;
                viewModel.suppressSourceButton = typedRule.SuppressSourceButton;
                // Stash the selected ids; the editor resolves them to
                // named ToggleTargets options once it has the sibling list.
                viewModel.PendingToggleTargetIds = [.. typedRule.TargetRuleIds];
                break;

            case ControlScriptRule typedRule:
                viewModel.kind = RuleKind.Script;
                viewModel.controlKey = typedRule.ControlKey;
                viewModel.scriptCode = typedRule.ScriptCode;
                viewModel.suppressScriptSourceInput = typedRule.SuppressSourceInput;
                break;
        }

        return viewModel;
    }

    public static MappingRuleViewModel CreateDefault(RuleKind kind)
    {
        return new()
        {
            kind = kind,
            name = kind switch
            {
                RuleKind.ButtonRemap => "Button Remap",
                RuleKind.ButtonAutofire => "Button Autofire",
                RuleKind.StickThreshold => "Stick Threshold",
                RuleKind.StickAutofire => "Stick Autofire",
                RuleKind.FreezeLastDirection => "Freeze Last Direction",
                RuleKind.MultiButtonAutofire => "Multi-Button Autofire",
                RuleKind.RuleToggle => "Rule Toggle",
                RuleKind.Script => "Control Script",
                _ => "Rule"
            },
            controlKey = kind == RuleKind.Script ? "South" : string.Empty
        };
    }

    private static IReadOnlyList<ButtonIdOption> BuildButtonOptions()
    {
        return [.. Enum.GetValues<ButtonId>().Select(button => new ButtonIdOption(button, ControlRuleMatcher.FormatButtonLabel(button)))];
    }

    private static IReadOnlyList<StickIdOption> BuildStickOptions()
    {
        return
        [
            new StickIdOption(StickId.Left, ControlRuleMatcher.FormatStickLabel(StickId.Left)),
            new StickIdOption(StickId.Right, ControlRuleMatcher.FormatStickLabel(StickId.Right))
        ];
    }

    private static IReadOnlyList<BlendModeOption> BuildBlendOptions()
    {
        return
        [
            new BlendModeOption(StickBlendMode.Replace, "Replace — overwrite target"),
            new BlendModeOption(StickBlendMode.Additive, "Additive — add to target")
        ];
    }

    private static IReadOnlyList<RuleModeOption> BuildModeOptions()
    {
        return
        [
            new RuleModeOption(RuleMode.Modify, "Modify"),
            new RuleModeOption(RuleMode.Passthrough, "Passthrough"),
            new RuleModeOption(RuleMode.DoNothing, "Ignore / block")
        ];
    }

    private static IReadOnlyList<RuleKindOption> BuildRuleKindOptions()
    {
        return
        [
            new RuleKindOption(RuleKind.ButtonRemap, "Button Remap", "Route one button to another.", "#4F8CFF"),
            new RuleKindOption(RuleKind.ButtonAutofire, "Button Autofire / Turbo", "Rapid-fire a button at a configurable rate.", "#F97316"),
            new RuleKindOption(RuleKind.StickThreshold, "Stick Threshold", "Deadzone and threshold shaping for a stick.", "#10B981"),
            new RuleKindOption(RuleKind.StickAutofire, "Stick Autofire", "Pulse one stick output from another stick.", "#A78BFA"),
            new RuleKindOption(RuleKind.FreezeLastDirection, "Freeze Last Direction", "Hold the last observed stick direction while a button is held.", "#00D4FF"),
            new RuleKindOption(RuleKind.MultiButtonAutofire, "Multi-Button Autofire", "Loop a sequence of button presses with per-step timing while held.", "#FB7185"),
            new RuleKindOption(RuleKind.RuleToggle, "Rule Toggle", "Press a button to enable/disable one or more other rules.", "#34D399"),
            new RuleKindOption(RuleKind.Script, "Control Script", "Store script text for a specific control.", "#FACC15")
        ];
    }

    private static string FormatScriptPreview(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "No script stored yet.";
        }

        var singleLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= 88 ? singleLine : singleLine[..85] + "...";
    }

    private static string FormatMode(RuleMode mode)
    {
        return mode switch
        {
            RuleMode.Modify => "modify",
            RuleMode.Passthrough => "passthrough",
            RuleMode.DoNothing => "ignore",
            _ => mode.ToString()
        };
    }

    private static string FormatSuffix(bool enabled, string text)
    {
        return enabled ? $" · {text}" : string.Empty;
    }

    private static bool TryParseDouble(string? value, out double parsed)
    {
        var normalized = value?.Trim().Replace(',', '.') ?? string.Empty;
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed);
    }
}
