using GameFlow.Core.Models;

namespace GameFlow.Core.Pipeline;

/// <summary>
/// Builds a complete DualSense effect report that SDL's PS5 driver accepts
/// through <c>SDL_SendGamepadEffect</c>.
///
/// <para>
/// Adaptive triggers have no portable API — SDL exposes rumble and the
/// LED, but resistance curves are a device-specific output report.
/// SDL's portable rumble and LED APIs are still called first so SDL's
/// internal state remains synchronized. This encoder then produces the
/// final, atomic device state containing those portable channels together
/// with the trigger commands. DualSense output reports are stateful: a
/// trigger-only report can restore audio haptics and stop steady rumble,
/// while a later portable report can clear the trigger effect. The combined
/// final write avoids both order-dependent failures. SDL owns the framing
/// (report id, and the CRC that Bluetooth requires but USB does not), which
/// is why this goes through SDL rather than a raw HID write.
/// </para>
///
/// <para>
/// The layout mirrors SDL's <c>DS5EffectsState_t</c>. It is a fixed wire
/// contract with the controller firmware: the offsets below are the
/// format, not a choice, and the enable bits are what make the firmware
/// look at a section at all — a correct trigger payload with the wrong
/// enable bit is silently ignored, which is the failure mode this is
/// easiest to get wrong in.
/// </para>
/// </summary>
public static class DualSenseEffectEncoder
{
    /// <summary>Size of DS5EffectsState_t. SDL copies this whole block into the output report.</summary>
    public const int EffectStateLength = 47;

    // Offsets within DS5EffectsState_t.
    private const int OffsetEnableBits1 = 0;
    private const int OffsetEnableBits2 = 1;
    private const int OffsetRumbleRight = 2;
    private const int OffsetRumbleLeft = 3;
    private const int OffsetMicLight = 8;
    private const int OffsetRightTriggerEffect = 10;
    private const int OffsetLeftTriggerEffect = 21;
    private const int OffsetEnableBits3 = 38;
    private const int OffsetPlayerLedBrightness = 42;
    private const int OffsetPlayerLeds = 43;
    private const int OffsetLedRed = 44;
    private const int OffsetLedGreen = 45;
    private const int OffsetLedBlue = 46;

    // Enable bits. Without the matching bit the firmware ignores that
    // section entirely, whatever it contains. The LED bits match SDL's
    // HIDAPI_DriverPS5_UpdateEffects and the kernel's hid-playstation
    // (valid_flag1 MIC_MUTE_LED / LIGHTBAR / PLAYER_INDICATOR, valid_flag2
    // LED brightness).
    private const byte Enable1LegacyRumble = 0x01;
    private const byte Enable1DisableAudioHaptics = 0x02;
    private const byte Enable1RightTrigger = 0x04;
    private const byte Enable1LeftTrigger = 0x08;
    private const byte Enable2MicLight = 0x01;
    private const byte Enable2Lightbar = 0x04;
    private const byte Enable2PlayerLeds = 0x10;
    private const byte Enable3PlayerLedBrightness = 0x01;
    private const byte Enable3EnhancedRumble = 0x04;

    /// <summary>
    /// Set alongside the player-LED bits: switch instantly rather than
    /// fading. SDL sets it too, and a fade on every animation step would
    /// smear a battery bar or a game's indicator changes.
    /// </summary>
    private const byte PlayerLedsInstant = 0x20;

    // SDL 3.4.2 uses the enhanced rumble lane for Sony pads on firmware
    // 0x0224+, for unknown Sony firmware (the Bluetooth fallback), and for
    // every DualSense Edge. All other PS5-driver devices use legacy emulation.
    public const ushort SonyVendorId = 0x054C;
    public const ushort DualSenseEdgeProductId = 0x0DF2;
    public const ushort EnhancedRumbleFirmwareMinimum = 0x0224;

    /// <summary>Trigger effect kinds as the DualSense firmware numbers them.</summary>
    private const byte TriggerOff = 0x05;
    private const byte TriggerFeedback = 0x21;
    private const byte TriggerWeapon = 0x25;
    private const byte TriggerVibration = 0x26;

    /// <summary>
    /// Writes a complete effect report into <paramref name="destination"/>,
    /// which must be at least <see cref="EffectStateLength"/> bytes.
    /// Returns false rather than throwing on a short buffer.
    /// </summary>
    /// <param name="destination">Destination for the complete report.</param>
    /// <param name="leftTrigger">Left trigger effect, or null to leave it unchanged.</param>
    /// <param name="rightTrigger">Right trigger effect, or null to leave it unchanged.</param>
    /// <param name="led">Lightbar colour, or null to leave it unchanged.</param>
    /// <param name="lowFrequencyRumble">Low-frequency motor amplitude.</param>
    /// <param name="highFrequencyRumble">High-frequency motor amplitude.</param>
    /// <param name="rumbleMode">Rumble encoding supported by the controller firmware.</param>
    /// <param name="playerLeds">
    /// Player-indicator bits (bit 0 = leftmost of five), or null to leave
    /// the LEDs as they are. 0 switches them all off.
    /// </param>
    /// <param name="playerLedBrightness">Brightness step for the player LEDs, or null to leave it.</param>
    /// <param name="micLed">Mic-mute LED state, or null to leave it.</param>
    public static bool TryWrite(
        Span<byte> destination,
        AdaptiveTriggerSettings? leftTrigger,
        AdaptiveTriggerSettings? rightTrigger,
        LightColor? led,
        ushort lowFrequencyRumble,
        ushort highFrequencyRumble,
        DualSenseRumbleMode rumbleMode,
        byte? playerLeds = null,
        IndicatorBrightnessLevel? playerLedBrightness = null,
        MicLedState? micLed = null)
    {
        if (destination.Length < EffectStateLength)
        {
            return false;
        }

        var report = destination[..EffectStateLength];
        report.Clear();

        byte enable1 = 0;
        byte enable2 = 0;

        // Match HIDAPI_DriverPS5_UpdateEffects in bundled SDL 3.4.2.
        // SDL first reduces its public 16-bit magnitudes to the high byte.
        // Legacy firmware then halves that byte to match Xbox controller
        // strength; enhanced firmware uses the full byte and enable-bits-3.
        var left = (byte)(lowFrequencyRumble >> 8);
        var right = (byte)(highFrequencyRumble >> 8);
        if (left != 0 || right != 0)
        {
            enable1 |= Enable1DisableAudioHaptics;
            if (rumbleMode == DualSenseRumbleMode.Enhanced)
            {
                report[OffsetEnableBits3] |= Enable3EnhancedRumble;
                report[OffsetRumbleLeft] = left;
                report[OffsetRumbleRight] = right;
            }
            else
            {
                enable1 |= Enable1LegacyRumble;
                report[OffsetRumbleLeft] = (byte)(left >> 1);
                report[OffsetRumbleRight] = (byte)(right >> 1);
            }
        }

        if (rightTrigger is not null && WriteTrigger(report[OffsetRightTriggerEffect..], rightTrigger))
        {
            enable1 |= Enable1RightTrigger;
        }

        if (leftTrigger is not null && WriteTrigger(report[OffsetLeftTriggerEffect..], leftTrigger))
        {
            enable1 |= Enable1LeftTrigger;
        }

        if (led is { } color)
        {
            enable2 |= Enable2Lightbar;
            report[OffsetLedRed] = color.R;
            report[OffsetLedGreen] = color.G;
            report[OffsetLedBlue] = color.B;
        }

        if (playerLeds is { } leds)
        {
            enable2 |= Enable2PlayerLeds;
            report[OffsetPlayerLeds] = (byte)((leds & 0x1F) | PlayerLedsInstant);
        }

        if (playerLedBrightness is { } level)
        {
            report[OffsetEnableBits3] |= Enable3PlayerLedBrightness;
            report[OffsetPlayerLedBrightness] = (byte)Math.Clamp((int)level, 0, 2);
        }

        if (micLed is { } mic)
        {
            enable2 |= Enable2MicLight;
            report[OffsetMicLight] = (byte)Math.Clamp((int)mic, 0, 2);
        }

        report[OffsetEnableBits1] = enable1;
        report[OffsetEnableBits2] = enable2;
        return true;
    }

    /// <summary>
    /// Mirrors SDL 3.4.2's <c>ctx-&gt;enhanced_rumble</c> decision so the
    /// final raw report uses the same lane and scaling as the portable call
    /// that preceded it.
    /// </summary>
    public static DualSenseRumbleMode ResolveRumbleMode(
        ushort vendorId,
        ushort productId,
        ushort firmwareVersion) =>
        vendorId == SonyVendorId &&
        (productId == DualSenseEdgeProductId ||
         firmwareVersion == 0 ||
         firmwareVersion >= EnhancedRumbleFirmwareMinimum)
            ? DualSenseRumbleMode.Enhanced
            : DualSenseRumbleMode.Legacy;

    /// <summary>
    /// Writes one 11-byte trigger block. Returns false when the mode
    /// produces no effect, so the caller can leave the enable bit clear
    /// rather than asking the firmware to apply nothing.
    /// </summary>
    private static bool WriteTrigger(Span<byte> block, AdaptiveTriggerSettings settings)
    {
        if (block.Length < 11)
        {
            return false;
        }

        block[..11].Clear();

        // Positions are ZONES, not a 0-255 travel value, and strengths are
        // one of eight levels. The firmware divides the pull into ten zones
        // and every extended effect addresses them by index.
        var startZone = ToZone(settings.StartPosition);
        var endZone = ToZone(settings.EndPosition);
        var level = ToLevel(settings.Strength);

        // An inverted or empty band would make the effect vanish with no
        // indication why, so it is corrected to a minimal valid band.
        if (endZone <= startZone)
        {
            endZone = (byte)Math.Min(9, startZone + 1);
        }

        // Nothing to ask for. Told to the caller as "no effect" so the
        // enable bit stays clear rather than arming an empty one.
        if (level == 0 && settings.Mode != AdaptiveTriggerMode.Off)
        {
            block[0] = TriggerOff;
            return true;
        }

        switch (settings.Mode)
        {
            case AdaptiveTriggerMode.Off:
                block[0] = TriggerOff;
                return true;

            // Uniform resistance from `startZone` onward.
            case AdaptiveTriggerMode.Feedback:
            case AdaptiveTriggerMode.SlopeFeedback:
            case AdaptiveTriggerMode.MultiplePositionFeedback:
                block[0] = TriggerFeedback;
                WriteZones(block, startZone, level);
                return true;

            // Resistance across a band that then gives way — a trigger pull.
            // Both edges travel as a two-bit mask rather than as positions.
            case AdaptiveTriggerMode.Weapon:
                {
                    // The firmware rejects a band starting before zone 2.
                    var weaponStart = (byte)Math.Clamp((int)startZone, 2, 8);
                    var weaponEnd = (byte)Math.Clamp((int)endZone, weaponStart + 1, 9);
                    var band = (ushort)((1 << weaponStart) | (1 << weaponEnd));

                    block[0] = TriggerWeapon;
                    block[1] = (byte)(band & 0xFF);
                    block[2] = (byte)((band >> 8) & 0xFF);
                    block[3] = (byte)(level - 1);
                    return true;
                }

            case AdaptiveTriggerMode.Vibration:
            case AdaptiveTriggerMode.MultiplePositionVibration:
                {
                    var frequency = (byte)Math.Clamp(settings.FrequencyHz, 0, 255);
                    if (frequency == 0)
                    {
                        // A buzz at no frequency is silence, and asking for
                        // one leaves the trigger armed but dead.
                        block[0] = TriggerOff;
                        return true;
                    }

                    block[0] = TriggerVibration;
                    WriteZones(block, startZone, level);

                    // Frequency is the NINTH parameter, not the third. This
                    // is what made the vibration modes produce nothing at
                    // all: written at block[3] it landed in the amplitude
                    // mask and left the frequency byte zero, so the pad was
                    // handed a buzz with no rate to buzz at.
                    block[9] = frequency;
                    return true;
                }

            default:
                return false;
        }
    }

    /// <summary>
    /// Fills the active-zone mask and the per-zone amplitude nibbles that
    /// every extended DualSense trigger effect shares.
    /// </summary>
    /// <remarks>
    /// Effects 0x21, 0x25 and 0x26 are the firmware's EXTENDED set, and
    /// they do not take a start byte and a strength byte. They take a
    /// ten-bit mask of which zones are engaged followed by three bits of
    /// amplitude per zone, packed little-endian across four bytes. Writing
    /// simple parameters into that layout does not fail — it addresses an
    /// arbitrary handful of zones at arbitrary strengths, which is why the
    /// resistance modes felt roughly plausible while vibration felt like
    /// nothing.
    /// </remarks>
    private static void WriteZones(Span<byte> block, byte startZone, byte level)
    {
        var amplitude = (byte)((level - 1) & 0x07);

        ushort activeZones = 0;
        uint amplitudeZones = 0;

        for (var zone = startZone; zone < ZoneCount; zone++)
        {
            activeZones |= (ushort)(1 << zone);
            amplitudeZones |= (uint)amplitude << (3 * zone);
        }

        block[1] = (byte)(activeZones & 0xFF);
        block[2] = (byte)((activeZones >> 8) & 0xFF);
        block[3] = (byte)(amplitudeZones & 0xFF);
        block[4] = (byte)((amplitudeZones >> 8) & 0xFF);
        block[5] = (byte)((amplitudeZones >> 16) & 0xFF);
        block[6] = (byte)((amplitudeZones >> 24) & 0xFF);
    }

    /// <summary>Zones the firmware divides the trigger pull into.</summary>
    private const int ZoneCount = 10;

    /// <summary>A 0..1 position as a zone index.</summary>
    private static byte ToZone(float unit) =>
        (byte)Math.Clamp((int)Math.Round(unit * (ZoneCount - 1)), 0, ZoneCount - 1);

    /// <summary>A 0..1 strength as one of the firmware's eight levels.</summary>
    private static byte ToLevel(float unit) =>
        (byte)Math.Clamp((int)Math.Round(unit * 8), 0, 8);
}

/// <summary>The two rumble encodings selected by SDL's PS5 HIDAPI driver.</summary>
public enum DualSenseRumbleMode
{
    Legacy,
    Enhanced,
}
