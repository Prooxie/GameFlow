using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Runtime.Templates;

namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// Maps a <see cref="VirtualControllerKind"/> to HIDMaestro catalog
/// profile ids, and classifies arbitrary catalog profiles back into a
/// kind family for theming.
///
/// <para>
/// Slugs are matched against the SDK's actual embedded catalog at
/// runtime (see <see cref="HidMaestroDynamic.TryResolveExistingProfileId"/>),
/// so each kind carries an ordered CANDIDATE list rather than one
/// hardcoded guess: the first id that actually exists in the loaded
/// catalog wins. This is what fixed "no virtual controller for
/// DualShock 4": the old single guess (<c>"dualshock4"</c>) does not
/// exist in the catalog — the shipped id is
/// <c>"dualshock-4-v1-full"</c> (verified against the SDK's own
/// example/SdkDemo/Program.cs) — so GetProfile returned null and
/// creation failed every time.
/// </para>
/// </summary>
public static class HidMaestroProfiles
{
    /// <summary>
    /// Ordered catalog-profile candidates for a kind; the first id
    /// present in the SDK's loaded catalog is used. Verified ids
    /// (SdkDemo / repo profiles/) come first; the rest are defensive
    /// aliases so a future SDK rename or an unverified generation slug
    /// degrades to the nearest family member instead of failing.
    /// </summary>
    public static IReadOnlyList<string> GetCandidateProfileIds(VirtualControllerKind kind) => kind switch
    {
        VirtualControllerKind.Xbox360 => ["xbox-360-wired", "xbox-360", "xbox360"],
        VirtualControllerKind.XboxOne => ["xbox-one-s-bt", "xbox-one-s", "xbox-one", "xbox-one-elite-series-2", "xbox-series-xs-bt"],
        VirtualControllerKind.XboxSeries => ["xbox-series-xs-bt", "xbox-series-x", "xbox-series", "xbox-one-s-bt"],
        VirtualControllerKind.DualShock4 => ["dualshock-4-v1-full", "dualshock-4-v2", "dualshock-4", "dualshock4", "ds4"],
        VirtualControllerKind.DualSense => ["dualsense", "dual-sense", "ds5"],
        // GenericDirectInput has no catalog slug at all: the profile is
        // BUILT at runtime via HMProfileBuilder + HidDescriptorBuilder
        // from the template's axis/button/POV counts. Returning an empty
        // list here is the signal for that path (the old code returned
        // "custom", a slug that does not exist, so GetProfile failed and
        // the generic kind could never create a device).
        // Verified slug for the Pro; the rest are plausible spellings —
        // the dynamic resolver also falls back to a keyword search over
        // the full 225-profile catalog, so slug drift degrades to a
        // slower lookup instead of a failure.
        VirtualControllerKind.SwitchPro => ["switch-pro", "nintendo-switch-pro", "switch-pro-controller", "pro-controller"],
        VirtualControllerKind.SteamController => ["steam-controller", "valve-steam-controller", "steam-controller-2015", "steam-controller-gordon"],
        VirtualControllerKind.GenericDirectInput => [],
        _ => ["xbox-360-wired"],
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

    /// <summary>
    /// Pre-activation (Vid, Pid) guess for a kind — the well-known pairs
    /// the corresponding catalog profiles advertise. Used only until a
    /// controller is actually created; once live, the sink reports the
    /// REAL identity read from the deployed profile (which also covers
    /// all 225 catalog profiles, not just these). Null for
    /// GenericDirectInput, whose identity comes from the template.
    /// </summary>
    public static (ushort Vid, ushort Pid)? ResolveHardwareSignature(VirtualControllerKind kind) => kind switch
    {
        VirtualControllerKind.Xbox360 => (0x045E, 0x028E),
        VirtualControllerKind.XboxOne => (0x045E, 0x02EA),
        VirtualControllerKind.XboxSeries => (0x045E, 0x0B13),
        VirtualControllerKind.DualShock4 => (0x054C, 0x09CC),
        VirtualControllerKind.DualSense => (0x054C, 0x0CE6),
        VirtualControllerKind.SwitchPro => (0x057E, 0x2009),
        VirtualControllerKind.SteamController => (0x28DE, 0x1102),
        _ => null,
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
