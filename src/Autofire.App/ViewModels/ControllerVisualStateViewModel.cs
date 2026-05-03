using System.Windows.Input;
using Autofire.Core.Enums;
using Autofire.Core.Models;
using CommunityToolkit.Mvvm.Input;

namespace Autofire.App.ViewModels;

/// <summary>
/// View-model for the live controller surface panel.
///
/// Performance contract:
///   • Never calls OnPropertyChanged(string.Empty) — that forces Avalonia to
///     re-evaluate every binding on every timer tick.
///   • Properties are grouped into three change tiers:
///       Identity — device name / connection state  (rare)
///       Style    — button labels, colours          (rare, only on style change)
///       Live     — opacities, stick offsets, etc.  (per-tick, only when data moved)
///   • String-valued live properties are cached to avoid per-tick allocations.
/// </summary>
public sealed class ControllerVisualStateViewModel : ViewModelBase
{
    // ─── Static property-name lists ───────────────────────────────────────────

    private static readonly string[] IdentityProps =
    [
        nameof(PanelTitle), nameof(DeviceName), nameof(IsConnected),
        nameof(ConnectionLabel), nameof(ConnectionBrush), nameof(PanelBorderBrush),
    ];

    private static readonly string[] StyleProps =
    [
        nameof(AccentBrush), nameof(AccentSoftBrush),
        nameof(ShellBrush), nameof(ShellHighlightBrush),
        nameof(SouthLegend), nameof(EastLegend), nameof(WestLegend), nameof(NorthLegend),
        nameof(SouthFaceBrush), nameof(EastFaceBrush), nameof(WestFaceBrush), nameof(NorthFaceBrush),
        nameof(LeftShoulderLabel), nameof(RightShoulderLabel),
        nameof(LeftTriggerLabel), nameof(RightTriggerLabel),
        nameof(BackLabel), nameof(StartLabel), nameof(GuideLabel),
        nameof(StyleLabel), nameof(ShowTouchpad), nameof(ShowShell),
    ];

    private static readonly string[] LiveProps =
    [
        nameof(SouthGlowOpacity), nameof(EastGlowOpacity),
        nameof(WestGlowOpacity), nameof(NorthGlowOpacity),
        nameof(LeftShoulderOpacity), nameof(RightShoulderOpacity),
        nameof(LeftTriggerButtonOpacity), nameof(RightTriggerButtonOpacity),
        nameof(BackOpacity), nameof(StartOpacity), nameof(GuideOpacity),
        nameof(TouchpadOpacity), nameof(TouchpadLabel),
        nameof(LeftStickClickOpacity), nameof(RightStickClickOpacity),
        nameof(DpadUpOpacity), nameof(DpadDownOpacity),
        nameof(DpadLeftOpacity), nameof(DpadRightOpacity),
        nameof(LeftStickUpOpacity), nameof(LeftStickDownOpacity),
        nameof(LeftStickLeftOpacity), nameof(LeftStickRightOpacity),
        nameof(RightStickUpOpacity), nameof(RightStickDownOpacity),
        nameof(RightStickLeftOpacity), nameof(RightStickRightOpacity),
        nameof(LeftStickOffsetX), nameof(LeftStickOffsetY),
        nameof(RightStickOffsetX), nameof(RightStickOffsetY),
        nameof(LeftTriggerBarHeight), nameof(RightTriggerBarHeight),
        nameof(LeftTriggerPercentText), nameof(RightTriggerPercentText),
        nameof(LeftStickValueText), nameof(RightStickValueText),
        nameof(TriggerSummaryText), nameof(PressedButtonsText),
        nameof(TouchSummaryText), nameof(ShowTouchFinger1), nameof(ShowTouchFinger2),
    ];

    // ─── State ────────────────────────────────────────────────────────────────

    private readonly Action<string> onElementSelected;
    private ControllerSnapshot snapshot = ControllerSnapshot.Empty("No controller detected");
    private ControllerVisualStyle visualStyle = ControllerVisualStyle.Auto;
    private string panelId = "physical";

    // Cached computed string values — updated only when the underlying data changes

    // ─── Construction ─────────────────────────────────────────────────────────

    public ControllerVisualStateViewModel(Action<string> onElementSelected)
    {
        this.onElementSelected = onElementSelected;
        SelectElementCommand = new RelayCommand<string>(SelectElement);
    }

    public ICommand SelectElementCommand { get; }

    // ─── Update (called every refresh tick from ShellViewModel) ───────────────

    public void Update(
        string panelId,
        string panelTitle,
        ControllerSnapshot newSnapshot,
        ControllerVisualStyle preferredStyle)
    {
        var newStyle = ResolveVisualStyle(preferredStyle, newSnapshot.DeviceName);

        var identityChanged =
            newSnapshot.DeviceName != snapshot.DeviceName ||
            panelTitle != PanelTitle;

        var styleChanged = newStyle != visualStyle;

        var liveChanged =
            newSnapshot.LeftStick != snapshot.LeftStick ||
            newSnapshot.RightStick != snapshot.RightStick ||
            Math.Abs(newSnapshot.LeftTrigger - snapshot.LeftTrigger) > 0.005f ||
            Math.Abs(newSnapshot.RightTrigger - snapshot.RightTrigger) > 0.005f ||
            newSnapshot.TouchContactCount != snapshot.TouchContactCount ||
            ButtonsChanged(newSnapshot.Buttons, snapshot.Buttons);

        // Nothing changed at all — skip entirely (common when idle)
        if (!identityChanged && !styleChanged && !liveChanged)
        {
            return;
        }

        this.panelId = panelId;
        PanelTitle = panelTitle;
        snapshot = newSnapshot;
        visualStyle = newStyle;

        if (liveChanged || styleChanged)
        {
            RebuildLiveCachedStrings();
            foreach (var prop in LiveProps)
            {
                OnPropertyChanged(prop);
            }
        }

        if (styleChanged)
        {
            foreach (var prop in StyleProps)
            {
                OnPropertyChanged(prop);
            }
        }

        if (identityChanged)
        {
            foreach (var prop in IdentityProps)
            {
                OnPropertyChanged(prop);
            }
        }
    }

    // ─── Identity properties ──────────────────────────────────────────────────

    public string PanelTitle { get; private set; } = "Controller";

    public string DeviceName =>
        string.IsNullOrWhiteSpace(snapshot.DeviceName) ? "No controller detected" : snapshot.DeviceName;

    public bool IsConnected =>
        !DeviceName.Contains("No controller", StringComparison.OrdinalIgnoreCase) &&
        !DeviceName.Contains("No compatible", StringComparison.OrdinalIgnoreCase) &&
        !DeviceName.Contains("Select a controller", StringComparison.OrdinalIgnoreCase) &&
        !DeviceName.Contains("disabled", StringComparison.OrdinalIgnoreCase) &&
        !DeviceName.Contains("Waiting", StringComparison.OrdinalIgnoreCase);

    public string ConnectionLabel => IsConnected ? "LIVE" : "IDLE";
    public string ConnectionBrush => IsConnected ? AccentBrush : "#64748B";
    public string PanelBorderBrush => IsConnected ? AccentBrush : "#334155";

    // ─── Style properties ─────────────────────────────────────────────────────

    public static string PanelBrush => "#09111B";

    public string AccentBrush => visualStyle switch
    {
        ControllerVisualStyle.Xbox => "#22C55E",
        ControllerVisualStyle.PlayStation4 => "#00B7FF",
        ControllerVisualStyle.PlayStation5 => "#4F8CFF",
        _ => "#00E5FF"
    };

    public string AccentSoftBrush => visualStyle switch
    {
        ControllerVisualStyle.Xbox => "#123722",
        ControllerVisualStyle.PlayStation4 => "#0E2C46",
        ControllerVisualStyle.PlayStation5 => "#142742",
        _ => "#0F2530"
    };

    public string ShellBrush => visualStyle switch
    {
        ControllerVisualStyle.Xbox => "#101B17",
        ControllerVisualStyle.PlayStation4 => "#101A2B",
        ControllerVisualStyle.PlayStation5 => "#0F172A",
        _ => "#0D1522"
    };

    public string ShellHighlightBrush => visualStyle switch
    {
        ControllerVisualStyle.Xbox => "#16241E",
        ControllerVisualStyle.PlayStation4 => "#14253D",
        ControllerVisualStyle.PlayStation5 => "#152132",
        _ => "#131D2B"
    };

    public string StyleLabel => visualStyle switch
    {
        ControllerVisualStyle.Xbox => "Xbox layout",
        ControllerVisualStyle.PlayStation4 => "DualShock 4 layout",
        ControllerVisualStyle.PlayStation5 => "DualSense layout",
        ControllerVisualStyle.None => "Minimal shell",
        _ => "Auto layout"
    };

    public bool ShowShell => visualStyle != ControllerVisualStyle.None;
    public bool ShowTouchpad => visualStyle is ControllerVisualStyle.PlayStation4 or ControllerVisualStyle.PlayStation5;

    private bool IsPlayStation => visualStyle is ControllerVisualStyle.PlayStation4 or ControllerVisualStyle.PlayStation5;

    public string SouthLegend => IsPlayStation ? "×" : "A";
    public string EastLegend => IsPlayStation ? "○" : "B";
    public string WestLegend => IsPlayStation ? "□" : "X";
    public string NorthLegend => IsPlayStation ? "△" : "Y";

    public string SouthFaceBrush => IsPlayStation ? "#4DAAFF" : "#22C55E";
    public string EastFaceBrush => IsPlayStation ? "#F97373" : "#EF4444";
    public string WestFaceBrush => IsPlayStation ? "#C084FC" : "#3B82F6";
    public string NorthFaceBrush => IsPlayStation ? "#93C5FD" : "#EAB308";

    public string LeftShoulderLabel => IsPlayStation ? "L1" : "LB";
    public string RightShoulderLabel => IsPlayStation ? "R1" : "RB";
    public string LeftTriggerLabel => IsPlayStation ? "L2" : "LT";
    public string RightTriggerLabel => IsPlayStation ? "R2" : "RT";
    public string BackLabel => visualStyle == ControllerVisualStyle.PlayStation5 ? "Create"
                              : IsPlayStation ? "Share" : "View";
    public string StartLabel => IsPlayStation ? "Options" : "Menu";
    public string GuideLabel => IsPlayStation ? "PS" : "Guide";

    // ─── Live properties — opacities ──────────────────────────────────────────

    public double SouthGlowOpacity => Active(snapshot.IsPressed(ButtonId.South));
    public double EastGlowOpacity => Active(snapshot.IsPressed(ButtonId.East));
    public double WestGlowOpacity => Active(snapshot.IsPressed(ButtonId.West));
    public double NorthGlowOpacity => Active(snapshot.IsPressed(ButtonId.North));
    public double LeftShoulderOpacity => Active(snapshot.IsPressed(ButtonId.LeftShoulder));
    public double RightShoulderOpacity => Active(snapshot.IsPressed(ButtonId.RightShoulder));
    public double LeftTriggerButtonOpacity => Active(snapshot.IsPressed(ButtonId.LeftTriggerButton));
    public double RightTriggerButtonOpacity => Active(snapshot.IsPressed(ButtonId.RightTriggerButton));
    public double BackOpacity => Active(snapshot.IsPressed(ButtonId.Back));
    public double StartOpacity => Active(snapshot.IsPressed(ButtonId.Start));
    public double GuideOpacity => Active(snapshot.IsPressed(ButtonId.Guide));
    public double TouchpadOpacity => Active(snapshot.TouchContactCount > 0 || snapshot.IsPressed(ButtonId.Touchpad));
    public double LeftStickClickOpacity => Active(snapshot.IsPressed(ButtonId.LeftStick));
    public double RightStickClickOpacity => Active(snapshot.IsPressed(ButtonId.RightStick));
    public double DpadUpOpacity => Active(snapshot.IsPressed(ButtonId.DpadUp));
    public double DpadDownOpacity => Active(snapshot.IsPressed(ButtonId.DpadDown));
    public double DpadLeftOpacity => Active(snapshot.IsPressed(ButtonId.DpadLeft));
    public double DpadRightOpacity => Active(snapshot.IsPressed(ButtonId.DpadRight));

    public double LeftStickUpOpacity => Active(snapshot.LeftStick.Y > 0.25f);
    public double LeftStickDownOpacity => Active(snapshot.LeftStick.Y < -0.25f);
    public double LeftStickLeftOpacity => Active(snapshot.LeftStick.X < -0.25f);
    public double LeftStickRightOpacity => Active(snapshot.LeftStick.X > 0.25f);
    public double RightStickUpOpacity => Active(snapshot.RightStick.Y > 0.25f);
    public double RightStickDownOpacity => Active(snapshot.RightStick.Y < -0.25f);
    public double RightStickLeftOpacity => Active(snapshot.RightStick.X < -0.25f);
    public double RightStickRightOpacity => Active(snapshot.RightStick.X > 0.25f);

    // ─── Live properties — analogue values ────────────────────────────────────

    public double LeftStickOffsetX => Math.Round(snapshot.LeftStick.X * 16d, 1);
    public double LeftStickOffsetY => Math.Round(-snapshot.LeftStick.Y * 16d, 1);
    public double RightStickOffsetX => Math.Round(snapshot.RightStick.X * 16d, 1);
    public double RightStickOffsetY => Math.Round(-snapshot.RightStick.Y * 16d, 1);

    public double LeftTriggerBarHeight => Math.Max(2d, Math.Round(Math.Clamp(snapshot.LeftTrigger, 0f, 1f) * 76d, 0));
    public double RightTriggerBarHeight => Math.Max(2d, Math.Round(Math.Clamp(snapshot.RightTrigger, 0f, 1f) * 76d, 0));

    // ─── Live properties — cached strings (avoid per-tick allocations) ────────

    public string LeftTriggerPercentText { get; private set; } = "0%";
    public string RightTriggerPercentText { get; private set; } = "0%";
    public string LeftStickValueText { get; private set; } = "+0.00, +0.00";
    public string RightStickValueText { get; private set; } = "+0.00, +0.00";
    public string TriggerSummaryText { get; private set; } = "0% / 0%";
    public string PressedButtonsText { get; private set; } = "—";
    public string TouchSummaryText { get; private set; } = "No touch";

    public string TouchpadLabel => snapshot.TouchContactCount switch
    {
        <= 0 => IsPlayStation ? "Touchpad" : "Surface",
        1 => "1 finger",
        _ => $"{snapshot.TouchContactCount} fingers"
    };

    public bool ShowTouchFinger1 => ShowTouchpad && snapshot.TouchContactCount >= 1;
    public bool ShowTouchFinger2 => ShowTouchpad && snapshot.TouchContactCount >= 2;

    // ─── Private helpers ──────────────────────────────────────────────────────

    private void RebuildLiveCachedStrings()
    {
        var lt = Math.Round(Math.Clamp(snapshot.LeftTrigger, 0f, 1f) * 100d, 0);
        var rt = Math.Round(Math.Clamp(snapshot.RightTrigger, 0f, 1f) * 100d, 0);
        LeftTriggerPercentText = $"{lt:0}%";
        RightTriggerPercentText = $"{rt:0}%";
        TriggerSummaryText = $"{lt:0}% / {rt:0}%";
        LeftStickValueText = $"{snapshot.LeftStick.X:+0.00;-0.00;+0.00}, {snapshot.LeftStick.Y:+0.00;-0.00;+0.00}";
        RightStickValueText = $"{snapshot.RightStick.X:+0.00;-0.00;+0.00}, {snapshot.RightStick.Y:+0.00;-0.00;+0.00}";

        TouchSummaryText = snapshot.TouchContactCount switch
        {
            <= 0 => "No touch",
            1 => "1 touch",
            _ => $"{snapshot.TouchContactCount} touches"
        };

        var pressed = snapshot.Buttons
            .Where(pair => pair.Value)
            .Select(pair => FormatButton(pair.Key))
            .Take(6)
            .ToArray();
        PressedButtonsText = pressed.Length == 0 ? "—" : string.Join(" · ", pressed);
    }

    private static bool ButtonsChanged(
        IReadOnlyDictionary<ButtonId, bool> a,
        IReadOnlyDictionary<ButtonId, bool> b)
    {
        foreach (var key in a.Keys)
        {
            var aVal = a.TryGetValue(key, out var av) && av;
            var bVal = b.TryGetValue(key, out var bv) && bv;
            if (aVal != bVal)
            {
                return true;
            }
        }
        return false;
    }

    private void SelectElement(string? parameter)
    {
        if (!string.IsNullOrWhiteSpace(parameter))
        {
            onElementSelected($"{panelId}:{parameter}");
        }
    }

    private static ControllerVisualStyle ResolveVisualStyle(ControllerVisualStyle preferred, string deviceName)
    {
        return preferred != ControllerVisualStyle.Auto
            ? preferred
            : deviceName.Contains("dualsense", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Contains("ps5", StringComparison.OrdinalIgnoreCase)
            ? ControllerVisualStyle.PlayStation5
            : deviceName.Contains("dualshock", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Contains("wireless controller", StringComparison.OrdinalIgnoreCase) ||
            deviceName.Contains("ps4", StringComparison.OrdinalIgnoreCase)
            ? ControllerVisualStyle.PlayStation4
            : deviceName.Contains("xbox", StringComparison.OrdinalIgnoreCase) ? ControllerVisualStyle.Xbox : ControllerVisualStyle.Auto;
    }

    private static string FormatButton(ButtonId b)
    {
        return b switch
        {
            ButtonId.South => "South",
            ButtonId.East => "East",
            ButtonId.West => "West",
            ButtonId.North => "North",
            ButtonId.LeftShoulder => "L1/LB",
            ButtonId.RightShoulder => "R1/RB",
            ButtonId.LeftTriggerButton => "L2/LT",
            ButtonId.RightTriggerButton => "R2/RT",
            ButtonId.Back => "Back",
            ButtonId.Start => "Start",
            ButtonId.Guide => "Guide",
            ButtonId.LeftStick => "L3",
            ButtonId.RightStick => "R3",
            ButtonId.DpadUp => "D↑",
            ButtonId.DpadDown => "D↓",
            ButtonId.DpadLeft => "D←",
            ButtonId.DpadRight => "D→",
            ButtonId.Touchpad => "Touch",
            _ => b.ToString()
        };
    }

    private static double Active(bool isActive)
    {
        return isActive ? 1.0d : 0.20d;
    }
}
