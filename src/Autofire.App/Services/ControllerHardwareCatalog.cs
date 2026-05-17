using Autofire.Core.Enums;

namespace Autofire.App.Services;

/// <summary>
/// Hardware-id (VID/PID) → <see cref="ControllerVisualStyle"/> lookup
/// used by the visual surface when its style is set to
/// <see cref="ControllerVisualStyle.Auto"/>. Looking up by VID/PID is
/// deterministic and language-independent, unlike the previous
/// approach of substring-matching the device-name string (which
/// failed when an SDL3 mapping reported a localised name).
///
/// <para>
/// Adding a new device is a one-line change: append a new
/// <c>(Vid, Pid, Style)</c> tuple to <see cref="KnownDevices"/>. Use
/// <c>Pid = 0</c> to match a whole vendor (e.g. all Microsoft Xbox
/// pads share VID <c>0x045E</c>).
/// </para>
///
/// <para>
/// The IDs encoded here come from public USB descriptors and are
/// widely distributed in open-source projects (libsdl, ViGEm,
/// JoyShockMapper). They are not proprietary.
/// </para>
/// </summary>
public static class ControllerHardwareCatalog
{
    /// <summary>
    /// Returns the inferred style for <paramref name="vendorId"/> /
    /// <paramref name="productId"/>, or
    /// <see cref="ControllerVisualStyle.Auto"/> when no entry matches.
    /// "Auto" here is the sentinel value for "I don't know" so the
    /// caller can fall back to its name-based heuristic.
    /// </summary>
    public static ControllerVisualStyle Resolve(ushort vendorId, ushort productId)
    {
        if (vendorId == 0)
        {
            return ControllerVisualStyle.Auto;
        }

        // Specific-PID entries beat vendor-only entries because the
        // table iterates top-down and Auto is the only "miss" return.
        foreach (var (vid, pid, style) in KnownDevices)
        {
            if (vid != vendorId)
            {
                continue;
            }

            if (pid == productId || pid == 0) // pid==0 → vendor-wide match
            {
                return style;
            }
        }

        return ControllerVisualStyle.Auto;
    }

    /// <summary>
    /// Public read-only view of the table — exposed so diagnostics
    /// dumps and tests can inspect it without reflection.
    /// </summary>
    public static IReadOnlyList<(ushort Vid, ushort Pid, ControllerVisualStyle Style)> All => KnownDevices;

    // Vendor IDs (centralised so the table below stays scannable).
    private const ushort SonyVid       = 0x054C;
    private const ushort MicrosoftVid  = 0x045E;
    private const ushort NintendoVid   = 0x057E;
    private const ushort ValveVid      = 0x28DE;
    private const ushort RazerVid      = 0x1532;
    private const ushort HoriVid       = 0x0F0D;
    private const ushort EightBitDoVid = 0x2DC8;

    /// <summary>
    /// The lookup table itself. Specific PIDs first, then vendor-wide
    /// fallbacks. Order matters because the iterator returns the
    /// first match.
    /// </summary>
    private static readonly (ushort Vid, ushort Pid, ControllerVisualStyle Style)[] KnownDevices =
    [
        // ─── PlayStation ───────────────────────────────────────────────
        // DualShock 3 (PS3)
        (SonyVid, 0x0268, ControllerVisualStyle.PlayStation3),

        // DualShock 4 (PS4) — original (CUH-ZCT1) and v2 (CUH-ZCT2)
        (SonyVid, 0x05C4, ControllerVisualStyle.PlayStation4),
        (SonyVid, 0x09CC, ControllerVisualStyle.PlayStation4),
        (SonyVid, 0x0BA0, ControllerVisualStyle.PlayStation4), // wireless dongle

        // DualSense (PS5) — base model and DualSense Edge
        (SonyVid, 0x0CE6, ControllerVisualStyle.PlayStation5),
        (SonyVid, 0x0DF2, ControllerVisualStyle.PlayStation5), // Edge

        // Sony vendor-wide fallback — any unlisted Sony pad → PS4 skin
        (SonyVid, 0x0000, ControllerVisualStyle.PlayStation4),

        // ─── Xbox / Microsoft ──────────────────────────────────────────
        // Xbox One controllers (a few common PIDs)
        (MicrosoftVid, 0x02D1, ControllerVisualStyle.Xbox), // Xbox One launch
        (MicrosoftVid, 0x02DD, ControllerVisualStyle.Xbox), // Xbox One S
        (MicrosoftVid, 0x02E3, ControllerVisualStyle.Xbox), // Elite Series 1
        (MicrosoftVid, 0x02EA, ControllerVisualStyle.Xbox), // Xbox One S BT
        (MicrosoftVid, 0x02FD, ControllerVisualStyle.Xbox), // Xbox One BT (alt)
        (MicrosoftVid, 0x0B00, ControllerVisualStyle.Xbox), // Elite Series 2
        (MicrosoftVid, 0x0B05, ControllerVisualStyle.Xbox), // Elite Series 2 BT
        (MicrosoftVid, 0x0B12, ControllerVisualStyle.Xbox), // Xbox Series X|S
        (MicrosoftVid, 0x0B13, ControllerVisualStyle.Xbox), // Xbox Series X|S BT

        // Microsoft vendor-wide fallback
        (MicrosoftVid, 0x0000, ControllerVisualStyle.Xbox),

        // ─── Nintendo ──────────────────────────────────────────────────
        // Switch Pro and Joy-Con — visually closest to "Auto" since
        // we don't yet have a Switch overlay. Mapped to Auto so the
        // programmatic minimal shell takes over rather than rendering
        // a misleading Xbox/PS skin.
        (NintendoVid, 0x0000, ControllerVisualStyle.Auto),

        // ─── Steam Controller / Steam Deck ─────────────────────────────
        (ValveVid, 0x0000, ControllerVisualStyle.Auto),

        // ─── Third-party Xbox-class pads (treat as Xbox visually) ──────
        (RazerVid,       0x0000, ControllerVisualStyle.Xbox),
        (HoriVid,        0x0000, ControllerVisualStyle.Xbox),
        (EightBitDoVid,  0x0000, ControllerVisualStyle.Xbox),
    ];
}
