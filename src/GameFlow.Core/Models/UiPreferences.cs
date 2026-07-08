using GameFlow.Core.Enums;

namespace GameFlow.Core.Models;

public sealed record UiPreferences
{
    public string LanguageCode { get; init; } = "en";
    public bool ShowPreviewPane { get; init; } = true;
    public string Theme { get; init; } = "System";
    public bool StartMinimized { get; init; }
    public ControllerVisualStyle PhysicalControllerStyle { get; init; } = ControllerVisualStyle.Auto;
    public ControllerVisualStyle VirtualControllerStyle { get; init; } = ControllerVisualStyle.PlayStation5;
    public bool ShowRawMonitor { get; init; } = true;

    // ─── Theme engine (per-style variant choices) ──────────────────────────
    //
    // Keyed by the registry-side id of an InstalledTheme (lower-cased,
    // hyphen-separated relative path, e.g. "dualsense-midnight-black").
    // Stored as separate fields per controller-style rather than as a
    // dictionary because JSON.NET handles primitive fields more
    // robustly across upgrades, and because the set of styles is fixed
    // at design time.
    //
    // Empty string ("the default") means "pick the first variant the
    // registry reports for that style".

    /// <summary>Variant id for DualSense (PlayStation5) themes.</summary>
    public string DualSenseVariantId { get; init; } = string.Empty;

    /// <summary>Variant id for DualShock 4 (PlayStation4) themes.</summary>
    public string DualShock4VariantId { get; init; } = string.Empty;

    /// <summary>Variant id for DualShock 3 (PlayStation3) themes.</summary>
    public string DualShock3VariantId { get; init; } = string.Empty;

    /// <summary>Variant id for Xbox themes (covers both 360 and Series X today).</summary>
    public string XboxVariantId { get; init; } = string.Empty;

    // ─── Panel background ──────────────────────────────────────────────────

    /// <summary>
    /// Brush string the controller-surface panels paint behind the
    /// art. Default "#09111B" matches the dark UI; "Transparent" gives
    /// the see-through effect on top of the window background. Any
    /// Avalonia-parseable colour string is accepted.
    /// </summary>
    public string ControllerPanelBackground { get; init; } = "#09111B";
}
