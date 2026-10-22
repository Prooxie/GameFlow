using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Runtime.Templates;

namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// Maps a <see cref="VirtualControllerKind"/> to its HIDMaestro catalog
/// profile id, and classifies arbitrary catalog profiles back into a
/// kind family for theming.
///
/// <para>
/// Each kind has exactly one default id, checked against the SDK's
/// shipped catalog by the adapter tests. The sink looks that id up in
/// the loaded catalog and nothing else: no alternative spellings, no
/// keyword search. A renamed profile then fails with the backend's own
/// error instead of quietly deploying a different pad. Exact ids matter
/// here — the old guess for DualShock 4 (<c>"dualshock4"</c>) was not a
/// catalog id at all (the shipped id is <c>"dualshock-4-v1-full"</c>),
/// which is why DS4 slots once never created a device.
/// </para>
/// </summary>
public static class HidMaestroProfiles
{
    /// <summary>
    /// The verified catalog id for a kind, or null for the generic kind,
    /// which has no catalog entry and is built from a shape instead.
    /// </summary>
    public static string? DefaultProfileId(VirtualControllerKind kind) => kind switch
    {
        VirtualControllerKind.Xbox360 => "xbox-360-wired",
        VirtualControllerKind.XboxOne => "xbox-one-s-bt",
        VirtualControllerKind.XboxSeries => "xbox-series-xs-bt",
        VirtualControllerKind.DualShock4 => "dualshock-4-v1-full",
        VirtualControllerKind.DualSense => "dualsense",
        VirtualControllerKind.SwitchPro => "switch-pro",
        VirtualControllerKind.SteamController => "steam-controller",
        VirtualControllerKind.GenericDirectInput => null,
        _ => "xbox-360-wired",
    };

    /// <summary>
    /// Classifies any catalog profile (by id and display name) into the
    /// nearest <see cref="VirtualControllerKind"/> family. Drives the
    /// dashboard theme: pick a DualSense-family profile and the virtual
    /// panel renders the PS5 silhouette with its skin variants, pick an
    /// Xbox-family profile and it renders that generation's layout,
    /// anything else falls to the generic family.
    /// </summary>
    public static VirtualControllerKind ClassifyFamily(string? profileId, string? profileName = null)
    {
        var haystack = $"{profileId} {profileName}".ToLowerInvariant();

        if (haystack.Contains("dualsense") || haystack.Contains("dual-sense") || haystack.Contains("ds5"))
        {
            return VirtualControllerKind.DualSense;
        }
        if (haystack.Contains("dualshock") || haystack.Contains("dual-shock") || haystack.Contains("ds4") || haystack.Contains("ds3"))
        {
            return VirtualControllerKind.DualShock4;
        }
        // Xbox generations — specific before generic so "xbox-series-xs"
        // doesn't bucket into the 360 fallback.
        if (haystack.Contains("series") && haystack.Contains("xbox") || haystack.Contains("xbsx"))
        {
            return VirtualControllerKind.XboxSeries;
        }
        if (haystack.Contains("xbox-one") || haystack.Contains("xbox one") || haystack.Contains("xboxone") || haystack.Contains("xbone"))
        {
            return VirtualControllerKind.XboxOne;
        }
        if (haystack.Contains("xbox") || haystack.Contains("xinput") || haystack.Contains("x360") || haystack.Contains("360"))
        {
            return VirtualControllerKind.Xbox360;
        }
        if (haystack.Contains("switch") && (haystack.Contains("pro") || haystack.Contains("controller")))
        {
            return VirtualControllerKind.SwitchPro;
        }
        if (haystack.Contains("steam") && haystack.Contains("controller"))
        {
            return VirtualControllerKind.SteamController;
        }

        return VirtualControllerKind.GenericDirectInput;
    }

    /// <summary>
    /// The dashboard silhouette that best represents a kind family —
    /// the "theme follows the selected output controller" mapping. Each
    /// kind maps to its own generation's style (the theme registry then
    /// falls back through the family when no theme is installed for the
    /// exact generation), and the generic kind maps to
    /// <see cref="ControllerVisualStyle.SimpleGamepad"/> — NOT
    /// <see cref="ControllerVisualStyle.None"/>, which means "render
    /// nothing" and was why generic slots showed an empty panel with a
    /// "no theme installed" message.
    /// </summary>
    public static ControllerVisualStyle ResolveVisualStyle(VirtualControllerKind kind) => kind switch
    {
        VirtualControllerKind.Xbox360 => ControllerVisualStyle.Xbox360,
        VirtualControllerKind.XboxOne => ControllerVisualStyle.XboxOne,
        VirtualControllerKind.XboxSeries => ControllerVisualStyle.XboxSeries,
        VirtualControllerKind.DualShock4 => ControllerVisualStyle.PlayStation4,
        VirtualControllerKind.DualSense => ControllerVisualStyle.PlayStation5,
        VirtualControllerKind.SwitchPro => ControllerVisualStyle.NintendoSwitch,
        VirtualControllerKind.SteamController => ControllerVisualStyle.SteamController,
        VirtualControllerKind.GenericDirectInput => ControllerVisualStyle.SimpleGamepad,
        _ => ControllerVisualStyle.Auto,
    };

    /// <summary>Human label for a kind — single source for every kind combo box and slot row.</summary>
    public static string LabelFor(VirtualControllerKind kind) => kind switch
    {
        VirtualControllerKind.Xbox360 => "Xbox 360",
        VirtualControllerKind.XboxOne => "Xbox One",
        VirtualControllerKind.XboxSeries => "Xbox Series X|S",
        VirtualControllerKind.DualShock4 => "DualShock 4",
        VirtualControllerKind.DualSense => "DualSense",
        VirtualControllerKind.SwitchPro => "Switch Pro",
        VirtualControllerKind.SteamController => "Steam Controller",
        VirtualControllerKind.GenericDirectInput => "Generic (DirectInput)",
        _ => "Controller",
    };

    /// <summary>Every kind offered by the output-kind pickers, in display order.</summary>
    public static IReadOnlyList<VirtualControllerKind> SelectableKinds { get; } =
    [
        VirtualControllerKind.Xbox360,
        VirtualControllerKind.XboxOne,
        VirtualControllerKind.XboxSeries,
        VirtualControllerKind.DualShock4,
        VirtualControllerKind.DualSense,
        VirtualControllerKind.SwitchPro,
        VirtualControllerKind.SteamController,
        VirtualControllerKind.GenericDirectInput,
    ];
}
