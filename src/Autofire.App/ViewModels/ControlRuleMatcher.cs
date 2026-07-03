using Autofire.Core.Enums;
using Autofire.Core.Models;
using Autofire.Core.Models.Rules;

namespace Autofire.App.ViewModels;

public static class ControlRuleMatcher
{
    // Ambient localizer (set once at startup by the shell). Card text is
    // rebuilt on every culture change, so a static hookup is sufficient —
    // and it keeps these formatting helpers static/pure to call.
    private static Autofire.Infrastructure.Localization.ILocalizationService? localizer;

    public static void UseLocalizer(Autofire.Infrastructure.Localization.ILocalizationService service) =>
        localizer = service;

    private static string L(string key, string fallback)
    {
        var value = localizer?[key];
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
            ? "Left stick (LX / LY)"
            : IsRightStickAnalog(key)
            ? "Right stick (RX / RY)"
            : key switch
            {
                "LeftTrigger.Analog" => "Left trigger (analog)",
                "RightTrigger.Analog" => "Right trigger (analog)",
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
            ? "Stick axes: deadzone, threshold, autofire, freeze-last-direction, script."
            : key switch
            {
                "LeftTrigger.Analog" or "RightTrigger.Analog" => L("MappedControlHintAnalogTrigger", "Analog trigger: passthrough, ignore, script, future analog remap."),
                "Touchpad" => "Touchpad: click / touch behaviour / script.",
                _ => L("MappedControlHintButton", "Button: passthrough, ignore, remap, autofire, script.")
            };
    }

    public static bool Matches(string selectionKey, MappingRule rule)
    {
        var key = NormalizeSelectionKey(selectionKey);

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
            _ => "South"
        });
    }

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
            _ => new ControlConfigurationEntryViewModel(L("MappedControlGenericTitle", "Rule"), rule.Name, "#64748B")
        };
    }

    public static string FormatButtonLabel(ButtonId button)
    {
        return button switch
        {
            ButtonId.South => "South (A / ×)",
            ButtonId.East => "East (B / ○)",
            ButtonId.West => "West (X / □)",
            ButtonId.North => "North (Y / △)",
            ButtonId.LeftShoulder => "L1 / LB",
            ButtonId.RightShoulder => "R1 / RB",
            ButtonId.LeftTriggerButton => "L2 / LT button",
            ButtonId.RightTriggerButton => "R2 / RT button",
            ButtonId.Back => "Back / Share / View",
            ButtonId.Start => "Start / Options / Menu",
            ButtonId.Guide => "Guide / PS",
            ButtonId.LeftStick => "L3 / Left stick click",
            ButtonId.RightStick => "R3 / Right stick click",
            ButtonId.DpadUp => "D-pad up",
            ButtonId.DpadDown => "D-pad down",
            ButtonId.DpadLeft => "D-pad left",
            ButtonId.DpadRight => "D-pad right",
            ButtonId.Touchpad => "Touchpad click",
            ButtonId.Paddle1 => "Paddle 1",
            ButtonId.Paddle2 => "Paddle 2",
            ButtonId.Paddle3 => "Paddle 3",
            ButtonId.Paddle4 => "Paddle 4",
            ButtonId.Misc1 => "Misc 1",
            _ => button.ToString()
        };
    }

    public static string FormatStickLabel(StickId stick)
    {
        return stick == StickId.Left ? "Left stick (LX / LY)" : "Right stick (RX / RY)";
    }

    public static bool TryResolveButtonId(string elementKey, out ButtonId button)
    {
        var key = NormalizeSelectionKey(elementKey);
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
