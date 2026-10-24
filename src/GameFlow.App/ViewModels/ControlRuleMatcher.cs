using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;

namespace GameFlow.App.ViewModels;

public static class ControlRuleMatcher
{
    // Ambient localizer (set once at startup by the shell). Card text is
    // rebuilt on every culture change, so a static hookup is sufficient —
    // and it keeps these formatting helpers static/pure to call.
    private static GameFlow.Infrastructure.Localization.ILocalizationService? localizer;

    public static void UseLocalizer(GameFlow.Infrastructure.Localization.ILocalizationService service) =>
        localizer = service;

    private static string L(string key, string fallback)
    {
        var value = localizer?[key] ?? UiTextResources.Get(key);
        return string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal)
            ? fallback
            : value!;
    }

    public static string NormalizeSelectionKey(string selectionKey)
    {
        var index = selectionKey.IndexOf(':');
        return index >= 0 ? selectionKey[(index + 1)..] : selectionKey;
    }

    public static string EnsurePhysicalSelectionKey(string selectionKey)
    {
        return selectionKey.Contains(':', StringComparison.Ordinal)
                ? selectionKey
                : $"physical:{selectionKey}";
    }

    public static string GetTitle(string selectionKey)
    {
        var key = NormalizeSelectionKey(selectionKey);

        return TryResolveButtonId(key, out var button)
            ? FormatButtonLabel(button)
            : IsLeftStickAnalog(key)
            ? FormatStickLabel(StickId.Left)
            : IsRightStickAnalog(key)
            ? FormatStickLabel(StickId.Right)
            : key switch
            {
                "LeftTrigger.Analog" => UiTextResources.Get("UiLeftTriggerAnalog"),
                "RightTrigger.Analog" => UiTextResources.Get("UiRightTriggerAnalog"),
                _ => key
            };
    }

    public static string GetHint(string selectionKey)
    {
        var key = NormalizeSelectionKey(selectionKey);

        return key.Equals("LeftStick.Button", StringComparison.OrdinalIgnoreCase) ||
            key.Equals("RightStick.Button", StringComparison.OrdinalIgnoreCase)
            ? L("MappedControlHintStickClick", "Stick click: passthrough, ignore, remap, autofire, script.")
            : IsLeftStickAnalog(key) || IsRightStickAnalog(key)
            ? UiTextResources.Get("UiStickHint")
            : key switch
            {
                "LeftTrigger.Analog" or "RightTrigger.Analog" => L("MappedControlHintAnalogTrigger", "Analog trigger: passthrough, ignore, script, future analog remap."),
                "Touchpad" => UiTextResources.Get("UiTouchHint"),
                _ => L("MappedControlHintButton", "Button: passthrough, ignore, remap, autofire, script.")
            };
    }

    public static bool Matches(string selectionKey, MappingRule rule)
    {
        var key = NormalizeSelectionKey(selectionKey);

        if (rule is AxisInvertRule invertRule)
        {
            return InvertTouchesControl(invertRule.Axes, key);
        }

        if (TryResolveButtonId(key, out var button))
        {
            return rule switch
            {
                ButtonRemapRule r => r.SourceButton == button || r.TargetButton == button,
                ButtonAutofireRule r => r.SourceButton == button || r.TargetButton == button,
                FreezeLastDirectionRule r => r.ActivationButton == button,
                ControlScriptRule r => string.Equals(r.ControlKey, key, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        return TryResolveStickId(key, out var stick)
            ? rule switch
            {
                StickThresholdRule r => r.TargetStick == stick,
                StickAutofireRule r => r.SourceStick == stick || r.TargetStick == stick,
                FreezeLastDirectionRule r => r.CaptureStick == stick || r.TargetStick == stick,
                ControlScriptRule r => string.Equals(r.ControlKey, key, StringComparison.OrdinalIgnoreCase),
                _ => false
            }
            : rule is ControlScriptRule scriptRule &&
               string.Equals(scriptRule.ControlKey, key, StringComparison.OrdinalIgnoreCase);
    }

    public static string GetPrimarySelectionKey(MappingRule rule)
    {
        return EnsurePhysicalSelectionKey(rule switch
        {
            ButtonRemapRule r => ToControlKey(r.SourceButton),
            ButtonAutofireRule r => ToControlKey(r.SourceButton),
            StickThresholdRule r => ToControlKey(r.TargetStick),
            StickAutofireRule r => ToControlKey(r.SourceStick),
            FreezeLastDirectionRule r => ToControlKey(r.ActivationButton),
            ControlScriptRule r => string.IsNullOrWhiteSpace(r.ControlKey) ? "South" : r.ControlKey,
            AxisInvertRule r => PrimaryInvertControl(r.Axes),
            _ => "South"
        });
    }

    /// <summary>Badge colour for axis-inversion rules, shared by the rule list and the control cards.</summary>
    public const string AxisInvertBadgeColor = "#E879F9";

    /// <summary>
    /// What an inversion flips, as short codes ("LX, RY, R2"), or
    /// <paramref name="whenNothing"/> when it flips nothing. Codes rather
    /// than words: they read the same in every language and match what the
    /// controller panels already print.
    /// </summary>
    public static string DescribeInvertedAxes(AxisInvert axes, string whenNothing)
    {
        var inverted = new List<string>(6);
        if (axes.LeftX is true) { inverted.Add("LX"); }
        if (axes.LeftY is true) { inverted.Add("LY"); }
        if (axes.RightX is true) { inverted.Add("RX"); }
        if (axes.RightY is true) { inverted.Add("RY"); }
        if (axes.LeftTrigger is true) { inverted.Add("L2"); }
        if (axes.RightTrigger is true) { inverted.Add("R2"); }
        return inverted.Count == 0 ? whenNothing : string.Join(", ", inverted);
    }

    /// <summary>
    /// Whether an inversion belongs on the card for <paramref name="key"/>:
    /// it has an opinion (on or explicitly off) about that stick or trigger.
    /// A rule with no opinion at all shows on every stick and trigger card,
    /// so a freshly added one can still be found and edited.
    /// </summary>
    private static bool InvertTouchesControl(AxisInvert axes, string key)
    {
        var hasAnyOpinion = axes.LeftX is not null || axes.LeftY is not null
            || axes.RightX is not null || axes.RightY is not null
            || axes.LeftTrigger is not null || axes.RightTrigger is not null;

        if (TryResolveStickId(key, out var stick))
        {
            return !hasAnyOpinion || (stick == StickId.Left
                ? axes.LeftX is not null || axes.LeftY is not null
                : axes.RightX is not null || axes.RightY is not null);
        }

        return key switch
        {
            "LeftTrigger.Analog" => !hasAnyOpinion || axes.LeftTrigger is not null,
            "RightTrigger.Analog" => !hasAnyOpinion || axes.RightTrigger is not null,
            _ => false
        };
    }

    /// <summary>The card an inversion is grouped under: the first control it flips, the right stick when it flips none.</summary>
    private static string PrimaryInvertControl(AxisInvert axes) =>
        axes.LeftX is true || axes.LeftY is true ? ToControlKey(StickId.Left)
        : axes.RightX is true || axes.RightY is true ? ToControlKey(StickId.Right)
        : axes.LeftTrigger is true ? "LeftTrigger.Analog"
        : axes.RightTrigger is true ? "RightTrigger.Analog"
        : ToControlKey(StickId.Right);

    public static ControlConfigurationEntryViewModel CreateEntry(MappingRule rule)
    {
        return rule switch
        {
            ButtonRemapRule r => new ControlConfigurationEntryViewModel(
                L("MappedControlRemapTitle", "Remap"),
                $"{FormatButtonLabel(r.SourceButton)} → {FormatButtonLabel(r.TargetButton)} · {FormatMode(r.Mode)}{FormatSuffix(r.SuppressSourceButton, L("MappedControlSuppressSource", "suppress src"))}",
                "#4F8CFF"),
            ButtonAutofireRule r => new ControlConfigurationEntryViewModel(
                L("MappedControlAutofireTitle", "Autofire"),
                $"{FormatButtonLabel(r.SourceButton)} → {FormatButtonLabel(r.TargetButton)} · {r.Timing.HoldMs}/{r.Timing.ReleaseMs} ms · {FormatMode(r.Mode)}{FormatSuffix(r.SuppressSourceButton, L("MappedControlSuppressSource", "suppress src"))}",
                "#F97316"),
            StickThresholdRule r => new ControlConfigurationEntryViewModel(
                L("MappedControlStickThresholdTitle", "Deadzone / Threshold"),
                $"{FormatStickLabel(r.TargetStick)} · {L("MappedControlDeadzoneWord", "deadzone")} {r.Deadzone:0.00} · {L("MappedControlFullWord", "full")} {r.FullAt:0.00} · {FormatMode(r.Mode)}{FormatSuffix(r.SuppressSourceStick, L("MappedControlSuppressRaw", "suppress raw"))}",
                "#10B981"),
            StickAutofireRule r => new ControlConfigurationEntryViewModel(
                L("MappedControlStickAutofireTitle", "Stick autofire"),
                $"{FormatStickLabel(r.SourceStick)} → {FormatStickLabel(r.TargetStick)} · {r.Timing.HoldMs}/{r.Timing.ReleaseMs} ms · {FormatMode(r.Mode)}{FormatSuffix(r.SuppressSourceStick, L("MappedControlSuppressSource", "suppress src"))}",
                "#A78BFA"),
            FreezeLastDirectionRule r => new ControlConfigurationEntryViewModel(
                L("MappedControlFreezeDirectionTitle", "Freeze direction"),
                $"{FormatButtonLabel(r.ActivationButton)} · {L("MappedControlCaptureWord", "capture")} {FormatStickLabel(r.CaptureStick)} → {FormatStickLabel(r.TargetStick)} · {FormatMode(r.Mode)}{FormatSuffix(r.SuppressActivationButton, L("MappedControlSuppressButton", "suppress btn"))}{FormatSuffix(r.SuppressCaptureStick, L("MappedControlSuppressStick", "suppress stick"))}",
                "#00D4FF"),
            ControlScriptRule r => new ControlConfigurationEntryViewModel(
                L("MappedControlScriptTitle", "Script"),
                string.IsNullOrWhiteSpace(r.ScriptCode)
                    ? $"{L("MappedControlScriptPlaceholder", "Script placeholder")}{FormatSuffix(r.SuppressSourceInput, L("MappedControlSuppressSource", "suppress src"))}"
                    : $"{Shorten(r.ScriptCode)}{FormatSuffix(r.SuppressSourceInput, L("MappedControlSuppressSource", "suppress src"))}",
                "#FACC15"),
            AxisInvertRule r => new ControlConfigurationEntryViewModel(
                L("MappedControlAxisInvertTitle", "Axis inversion"),
                $"{DescribeInvertedAxes(r.Axes, L("MappedControlAxisInvertNothing", "nothing inverted"))} · {FormatMode(r.Mode)}{FormatSuffix(r.Axes.ShowInLayout is true, L("MappedControlAxisInvertShown", "shown on layout"))}",
                AxisInvertBadgeColor),
            _ => new ControlConfigurationEntryViewModel(L("MappedControlGenericTitle", "Rule"), rule.Name, "#64748B")
        };
    }

    public static string FormatButtonLabel(ButtonId button) => UiTextResources.EnumValue(button);

    public static string FormatStickLabel(StickId stick) => UiTextResources.EnumValue(stick);

    public static bool TryResolveButtonId(string elementKey, out ButtonId button)
    {
        var key = NormalizeSelectionKey(elementKey);

        // "LeftStick" is the analog stick's key; its click is
        // "LeftStick.Button". Without this check the bare key fell through
        // to Enum.TryParse below and came back as ButtonId.LeftStick (L3), so
        // the stick card was treated as the L3 button: stick rules never
        // matched it and a rule added from it was pre-filled as a button rule.
        if (IsLeftStickAnalog(key) || IsRightStickAnalog(key))
        {
            button = ButtonId.None;
            return false;
        }

        switch (key.ToLowerInvariant())
        {
            case "lefttrigger.button":
                button = ButtonId.LeftTriggerButton;
                return true;
            case "righttrigger.button":
                button = ButtonId.RightTriggerButton;
                return true;
            case "leftstick.button":
                button = ButtonId.LeftStick;
                return true;
            case "rightstick.button":
                button = ButtonId.RightStick;
                return true;
        }

        return Enum.TryParse(key.Split('.', 2)[0], true, out button);
    }

    public static bool TryResolveStickId(string elementKey, out StickId stick)
    {
        var key = NormalizeSelectionKey(elementKey);
        if (IsLeftStickAnalog(key))
        {
            stick = StickId.Left;
            return true;
        }

        if (IsRightStickAnalog(key))
        {
            stick = StickId.Right;
            return true;
        }

        stick = default;
        return false;
    }

    public static string ToControlKey(ButtonId button)
    {
        return button switch
        {
            ButtonId.LeftTriggerButton => "LeftTrigger.Button",
            ButtonId.RightTriggerButton => "RightTrigger.Button",
            ButtonId.LeftStick => "LeftStick.Button",
            ButtonId.RightStick => "RightStick.Button",
            _ => button.ToString()
        };
    }

    public static string ToControlKey(StickId stick)
    {
        return stick == StickId.Left ? "LeftStick" : "RightStick";
    }

    private static bool IsLeftStickAnalog(string key)
    {
        return key.StartsWith("LeftStick", StringComparison.OrdinalIgnoreCase)
               && !key.EndsWith(".Button", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRightStickAnalog(string key)
    {
        return key.StartsWith("RightStick", StringComparison.OrdinalIgnoreCase)
               && !key.EndsWith(".Button", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatMode(RuleMode mode)
    {
        return mode switch
        {
            RuleMode.Modify => L("RuleModeModify", "modify"),
            RuleMode.Passthrough => L("RuleModePassthrough", "passthrough"),
            RuleMode.DoNothing => L("RuleModeIgnore", "ignore"),
            _ => mode.ToString()
        };
    }

    private static string FormatSuffix(bool enabled, string text)
    {
        return enabled ? $" · {text}" : string.Empty;
    }

    private static string Shorten(string value)
    {
        var singleLine = value.Replace("\r", " ").Replace("\n", " ").Trim();
        return singleLine.Length <= 80 ? singleLine : singleLine[..77] + "...";
    }
}
