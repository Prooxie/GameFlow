using GameFlow.Core.Enums;

namespace GameFlow.Core.Models;

/// <summary>
/// Maps known controller hardware (VID/PID pairs) to a
/// <see cref="ControllerVisualStyle"/> so the UI can pick a brand-
/// accurate surface automatically. Returns <see cref="ControllerVisualStyle.Auto"/>
/// when nothing matches — the caller then falls back to its name
/// heuristic and finally to the generic programmatic fallback.
///
/// <para>
/// The table covers the families the project ships layouts for:
/// PlayStation 3/4/5, Xbox 360 / One / Series, Nintendo Switch,
/// Steam Controller, Steam Deck. Arcade sticks and generic gamepads
/// don't have a canonical signature — they're identified via the name
/// heuristic in <c>ControllerVisualStateViewModel</c>.
/// </para>
/// </summary>
public static class ControllerHardwareCatalog
{
    /// <summary>
    /// Returns the brand-specific style for the given VID/PID, or
    /// <see cref="ControllerVisualStyle.Auto"/> if the pair is unknown.
    /// Comparisons are exact — <c>0x0000:0x0000</c> always returns
    /// <see cref="ControllerVisualStyle.Auto"/>.
    /// </summary>
    public static ControllerVisualStyle Resolve(ushort vendorId, ushort productId)
    {
        if (vendorId == 0 && productId == 0)
        {
            return ControllerVisualStyle.Auto;
        }

        return (vendorId, productId) switch
        {
            // ── Sony (0x054C) ──────────────────────────────────────
            (0x054C, 0x0268) => ControllerVisualStyle.PlayStation3,        // DualShock 3
            (0x054C, 0x042F) => ControllerVisualStyle.PlayStation3,        // Navigation Controller
            (0x054C, 0x05C4) => ControllerVisualStyle.PlayStation4,        // DS4 v1
            (0x054C, 0x09CC) => ControllerVisualStyle.PlayStation4,        // DS4 v2
            (0x054C, 0x0BA0) => ControllerVisualStyle.PlayStation4,        // DS4 wireless adapter
            (0x054C, 0x0CE6) => ControllerVisualStyle.PlayStation5,        // DualSense
            (0x054C, 0x0DF2) => ControllerVisualStyle.PlayStation5,        // DualSense Edge

            // ── Microsoft Xbox (0x045E) ────────────────────────────
            (0x045E, 0x028E) => ControllerVisualStyle.Xbox360,             // 360 wired
            (0x045E, 0x028F) => ControllerVisualStyle.Xbox360,             // 360 wireless adapter
            (0x045E, 0x0291) => ControllerVisualStyle.Xbox360,             // 360 wireless receiver
            (0x045E, 0x02A1) => ControllerVisualStyle.Xbox360,             // 360 controller (legacy)
            (0x045E, 0x02D1) => ControllerVisualStyle.XboxOne,             // Xbox One
            (0x045E, 0x02DD) => ControllerVisualStyle.XboxOne,             // Xbox One (fw 2015)
            (0x045E, 0x02E3) => ControllerVisualStyle.XboxOne,             // Xbox One Elite v1
            (0x045E, 0x02EA) => ControllerVisualStyle.XboxOne,             // Xbox One S
            (0x045E, 0x02FD) => ControllerVisualStyle.XboxOne,             // Xbox One S (BT)
            (0x045E, 0x0B00) => ControllerVisualStyle.XboxSeries,          // Elite v2
            (0x045E, 0x0B05) => ControllerVisualStyle.XboxSeries,          // Elite v2 (BT)
            (0x045E, 0x0B12) => ControllerVisualStyle.XboxSeries,          // Series X|S
            (0x045E, 0x0B13) => ControllerVisualStyle.XboxSeries,          // Series X|S (BT)
            (0x045E, 0x0B20) => ControllerVisualStyle.XboxSeries,          // Series adaptive
            (0x045E, 0x0B22) => ControllerVisualStyle.XboxSeries,          // Elite v2 Core

            // ── Nintendo (0x057E) ─────────────────────────────────
            (0x057E, 0x2006) => ControllerVisualStyle.NintendoSwitch,      // Joy-Con L
            (0x057E, 0x2007) => ControllerVisualStyle.NintendoSwitch,      // Joy-Con R
            (0x057E, 0x2009) => ControllerVisualStyle.NintendoSwitch,      // Switch Pro
            (0x057E, 0x200E) => ControllerVisualStyle.NintendoSwitch,      // Charging Grip
            (0x057E, 0x2017) => ControllerVisualStyle.NintendoSwitch,      // SNES Online

            // ── Valve (0x28DE) ────────────────────────────────────
            (0x28DE, 0x1102) => ControllerVisualStyle.SteamController,     // Steam Controller (wired)
            (0x28DE, 0x1142) => ControllerVisualStyle.SteamController,     // Steam Controller (dongle)
            (0x28DE, 0x1205) => ControllerVisualStyle.SteamDeck,           // Steam Deck

            _ => ControllerVisualStyle.Auto,
        };
    }

    /// <summary>
    /// True when this hardware carries a finger-tracking touch surface
    /// that SDL reports through <c>SDL_GetGamepadTouchpadFinger</c> —
    /// the DualShock 4 and DualSense families, plus Valve's trackpad
    /// hardware.
    ///
    /// <para>
    /// Answered from VID/PID rather than by asking SDL, deliberately:
    /// <c>SDL_GetNumGamepadTouchpads</c> needs an OPEN gamepad handle,
    /// and the device catalog enumerates pads it has not opened (opening
    /// each one just to probe would claim hardware the user hasn't
    /// assigned to a slot yet). Unknown hardware answers false, so the
    /// Touchpad tab stays hidden rather than appearing on a pad that
    /// would report nothing.
    /// </para>
    /// </summary>
    public static bool HasTouchpadSurface(ushort vendorId, ushort productId) =>
        Resolve(vendorId, productId) is ControllerVisualStyle.PlayStation4
            or ControllerVisualStyle.PlayStation5
            or ControllerVisualStyle.SteamController
            or ControllerVisualStyle.SteamDeck;

    // Shorthands for the feature table below.
    private const ControllerFeatures Rumble = ControllerFeatures.Rumble;
    private const ControllerFeatures Battery = ControllerFeatures.Battery;
    private const ControllerFeatures Motion = ControllerFeatures.Motion;
    private const ControllerFeatures PlayerLeds = ControllerFeatures.PlayerLeds;

    /// <summary>
    /// What this hardware family can do, from VID/PID alone.
    ///
    /// <para>
    /// The FALLBACK, not the answer. When SDL has the pad open it reports
    /// the LED and rumble capabilities its driver actually found (see
    /// <see cref="MergeDetected"/>), and that wins — the same DualShock 4
    /// can lose its lightbar behind a driver that does not expose it. The
    /// table exists because the tuning editor has to show the right
    /// sections for a pad that is assigned but unplugged, and for one that
    /// has not been opened yet.
    /// </para>
    ///
    /// <para>
    /// Unknown hardware with a real identity answers
    /// <see cref="ControllerFeatures.Rumble"/>: practically every pad that
    /// is not in the table is a generic XInput/DirectInput clone, and
    /// rumble is the one output those reliably have. Only a missing
    /// identity (0000:0000) answers <see cref="ControllerFeatures.None"/>,
    /// meaning "unknown — show everything".
    /// </para>
    /// </summary>
    public static ControllerFeatures DefaultFeatures(ushort vendorId, ushort productId)
    {
        if (vendorId == 0 && productId == 0)
        {
            return ControllerFeatures.None;
        }

        return (vendorId, productId) switch
        {
            // DualShock 3: four player LEDs and the two motors. The
            // Navigation controller has neither motor nor LED row worth
            // driving, only a battery.
            (0x054C, 0x0268) => Rumble | PlayerLeds | Motion | Battery,
            (0x054C, 0x042F) => Battery,

            // Wired 360 pads have no battery; the wireless receiver does.
            // The ring of light is assigned by Windows, not by an app.
            (0x045E, 0x028E) => Rumble,

            // Switch: Joy-Con R and the Pro controller carry the Home-button
            // LED; the SNES Online pad has neither motors nor motion.
            (0x057E, 0x2006) => Rumble | PlayerLeds | Motion | Battery,
            (0x057E, 0x2007) => Rumble | PlayerLeds | ControllerFeatures.MonoLight | Motion | Battery,
            (0x057E, 0x2009) => Rumble | PlayerLeds | ControllerFeatures.MonoLight | Motion | Battery,
            (0x057E, 0x2017) => PlayerLeds | Battery,

            _ => Resolve(vendorId, productId) switch
            {
                ControllerVisualStyle.PlayStation5 =>
                    Rumble | ControllerFeatures.AdaptiveTriggers | ControllerFeatures.RgbLight
                    | PlayerLeds | ControllerFeatures.MicLed
                    | ControllerFeatures.Touchpad | Motion | Battery,

                ControllerVisualStyle.PlayStation4 =>
                    Rumble | ControllerFeatures.RgbLight | ControllerFeatures.LightbarFlash
                    | ControllerFeatures.Touchpad | Motion | Battery,

                ControllerVisualStyle.Xbox360 => Rumble | Battery,

                ControllerVisualStyle.XboxOne or ControllerVisualStyle.XboxSeries =>
                    Rumble | ControllerFeatures.TriggerRumble | Battery,

                ControllerVisualStyle.NintendoSwitch => Rumble | PlayerLeds | Motion | Battery,

                ControllerVisualStyle.SteamController =>
                    Rumble | ControllerFeatures.Touchpad | Motion | Battery,

                ControllerVisualStyle.SteamDeck =>
                    Rumble | ControllerFeatures.Touchpad | Motion,

                _ => Rumble,
            },
        };
    }

    /// <summary>
    /// Capabilities SDL reports for an OPEN pad. These are the ones it can
    /// answer authoritatively; everything else still comes from the table.
    /// </summary>
    public const ControllerFeatures SdlDetectable =
        ControllerFeatures.Rumble | ControllerFeatures.TriggerRumble
        | ControllerFeatures.RgbLight | ControllerFeatures.MonoLight | ControllerFeatures.PlayerLeds;

    /// <summary>
    /// Combines what SDL detected with the catalog: SDL decides the bits it
    /// can see (<see cref="SdlDetectable"/>), the catalog fills the bits SDL
    /// has no property for (mic LED, flash, battery) and anything else the
    /// detector also found is kept. A <see cref="ControllerFeatures.None"/>
    /// detection means "not opened" and returns the catalog untouched.
    /// </summary>
    public static ControllerFeatures MergeDetected(
        ControllerFeatures detected, ushort vendorId, ushort productId)
    {
        var catalog = DefaultFeatures(vendorId, productId);
        if (detected == ControllerFeatures.None)
        {
            return catalog;
        }

        return detected | (catalog & ~SdlDetectable);
    }

    /// <summary>
    /// A short family name for the hardware ("DualSense", "Xbox Series"),
    /// or <see langword="null"/> when the pair is unknown. Used to label
    /// what is unique about a pad without inventing a name for a clone.
    /// </summary>
    public static string? FamilyName(ushort vendorId, ushort productId) =>
        (vendorId, productId) switch
        {
            (0x054C, 0x0DF2) => "DualSense Edge",
            (0x054C, 0x0CE6) => "DualSense",
            (0x054C, 0x042F) => "PlayStation Navigation",
            (0x057E, 0x2006) => "Joy-Con (L)",
            (0x057E, 0x2007) => "Joy-Con (R)",
            (0x057E, 0x2009) => "Switch Pro Controller",
            (0x057E, 0x2017) => "SNES Controller",
            _ => Resolve(vendorId, productId) switch
            {
                ControllerVisualStyle.PlayStation3 => "DualShock 3",
                ControllerVisualStyle.PlayStation4 => "DualShock 4",
                ControllerVisualStyle.PlayStation5 => "DualSense",
                ControllerVisualStyle.Xbox360 => "Xbox 360",
                ControllerVisualStyle.XboxOne => "Xbox One",
                ControllerVisualStyle.XboxSeries => "Xbox Series",
                ControllerVisualStyle.NintendoSwitch => "Nintendo Switch",
                ControllerVisualStyle.SteamController => "Steam Controller",
                ControllerVisualStyle.SteamDeck => "Steam Deck",
                _ => null,
            },
        };
}
