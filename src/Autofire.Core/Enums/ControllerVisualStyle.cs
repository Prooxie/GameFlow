namespace Autofire.Core.Enums;

/// <summary>
/// Visual style of a controller surface. Drives theme selection, brand-
/// coloured accent colours and the programmatic fallback drawing when
/// no theme asset is installed for the style.
///
/// <para>
/// Numeric values are pinned so persisted <c>UiPreferences</c> JSON
/// (which serializes the enum as an integer by default) keeps loading
/// the same style after new values are added. <see cref="None"/>,
/// <see cref="Auto"/>, the PlayStation* family, and <see cref="Xbox"/>
/// retain their pre-existing slots; everything else is additive.
/// </para>
/// </summary>
public enum ControllerVisualStyle
{
    /// <summary>No style — render nothing.</summary>
    None = 0,

    /// <summary>Pick automatically from the device's VID/PID, then
    /// fall back to a name heuristic.</summary>
    Auto = 1,

    // ─── Sony PlayStation family ─────────────────────────────────
    PlayStation3 = 10,
    PlayStation4 = 11,
    PlayStation5 = 12,

    // ─── Microsoft Xbox family ───────────────────────────────────
    /// <summary>Legacy / generic Xbox. Kept for backward compatibility
    /// with persisted preferences from earlier builds where the
    /// generation wasn't differentiated.</summary>
    Xbox       = 20,
    Xbox360    = 21,
    XboxOne    = 22,
    XboxSeries = 23,

    // ─── Nintendo ────────────────────────────────────────────────
    NintendoSwitch = 30,

    // ─── Valve ───────────────────────────────────────────────────
    SteamController = 40,
    SteamDeck       = 41,

    // ─── Generic / other ─────────────────────────────────────────
    SimpleGamepad = 50,
    Arcade        = 51,
}
