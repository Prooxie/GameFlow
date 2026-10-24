namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// Decodes the two output shapes HIDMaestro documents: XInput's raw
/// vibration packet and profile-decoded semantic motor fields.
/// </summary>
internal static class HidMaestroRumbleDecoder
{
    /// <summary>
    /// XUSB normally publishes five bytes
    /// <c>[command, size, low, high, trailer]</c>. HIDMaestro also allows
    /// the four-byte native <c>XINPUT_VIBRATION</c> struct, whose two
    /// magnitudes are little-endian 16-bit values.
    /// </summary>
    public static bool TryDecodeXInput(
        ReadOnlySpan<byte> data,
        out double lowFrequency,
        out double highFrequency)
    {
        lowFrequency = 0;
        highFrequency = 0;

        if (data.Length == 5)
        {
            lowFrequency = data[2] / 255d;
            highFrequency = data[3] / 255d;
            return true;
        }

        if (data.Length == 4)
        {
            lowFrequency = (data[0] | (data[1] << 8)) / (double)ushort.MaxValue;
            highFrequency = (data[2] | (data[3] << 8)) / (double)ushort.MaxValue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sony and Switch profiles expose decoded <c>leftMotor</c> and
    /// <c>rightMotor</c> fields through HIDMaestro's OutputDecoded event.
    /// Sony profiles also expose their output-validity flags. Those flags
    /// must authorize the motor fields: every profile decoder emits motor
    /// entries even for an LED- or adaptive-trigger-only report, where the
    /// zero bytes are padding rather than a request to stop rumble.
    ///
    /// <para>
    /// DualShock 4 uses bit 0 of <c>validFlag0</c> for rumble. DualSense
    /// uses bits 0-1 of <c>validFlag0</c> for legacy/audio-haptic rumble and
    /// bit 2 of <c>validFlag2</c> for the enhanced rumble lane. SDL's
    /// genuine DualSense zero-stop report has all three validity bytes
    /// clear, so that otherwise-empty shape is accepted as a stop. Reports
    /// carrying only LED or trigger validity are ignored. Switch semantic
    /// events have no validity fields and retain the original behavior.
    /// </para>
    ///
    /// Missing motor sides are treated as zero so a one-motor report still
    /// carries an unambiguous complete state.
    /// </summary>
    public static bool TryDecodeSemanticFields(
        IReadOnlyDictionary<string, object> fields,
        out double lowFrequency,
        out double highFrequency)
    {
        ArgumentNullException.ThrowIfNull(fields);

        var hasLeft = TryGet(fields, "leftMotor", out var left);
        var hasRight = TryGet(fields, "rightMotor", out var right);

        if (!hasLeft && !hasRight)
        {
            lowFrequency = 0;
            highFrequency = 0;
            return false;
        }

        var hasValid0 = TryGet(fields, "validFlag0", out var valid0Value);
        var hasValid1 = TryGet(fields, "validFlag1", out var valid1Value);
        var hasValid2 = TryGet(fields, "validFlag2", out var valid2Value);

        // HIDMaestro's Switch decoder publishes only the two motor fields.
        // No validity fields means this is not one of the profile-decoded
        // Sony reports and the named motor values are authoritative.
        if (hasValid0 || hasValid1 || hasValid2)
        {
            byte valid0 = 0;
            byte valid1 = 0;
            byte valid2 = 0;
            if ((hasValid0 && !TryToByte(valid0Value, out valid0))
                || (hasValid1 && !TryToByte(valid1Value, out valid1))
                || (hasValid2 && !TryToByte(valid2Value, out valid2)))
            {
                lowFrequency = 0;
                highFrequency = 0;
                return false;
            }

            var flag0 = valid0;
            var flag1 = valid1;
            var flag2 = valid2;
            var isDualSense = hasValid2
                || Contains(fields, "leftTriggerEffect")
                || Contains(fields, "rightTriggerEffect");

            if (isDualSense)
            {
                var explicitlyCarriesRumble = (flag0 & 0x03) != 0 || (flag2 & 0x04) != 0;
                var isStandaloneZeroStop = flag0 == 0 && flag1 == 0 && flag2 == 0
                    && ToUnit(left) == 0d && ToUnit(right) == 0d;

                if (!explicitlyCarriesRumble && !isStandaloneZeroStop)
                {
                    lowFrequency = 0;
                    highFrequency = 0;
                    return false;
                }
            }
            else if ((flag0 & 0x01) == 0)
            {
                // DS4 bit 0 is the rumble-valid bit. LED-only packets set
                // other bits while leaving the decoded motor bytes at zero.
                lowFrequency = 0;
                highFrequency = 0;
                return false;
            }
        }

        lowFrequency = hasLeft ? ToUnit(left) : 0d;
        highFrequency = hasRight ? ToUnit(right) : 0d;
        return true;
    }

    // Sony output-report validity bits for the light sections. The motor
    // bits are handled above; these follow the same rule — a section is
    // only a request when its own bit is set, because every report carries
    // every field and the unrequested ones are zero padding.
    //
    // DualSense (validFlag1 / validFlag2), as in SDL's
    // HIDAPI_DriverPS5_UpdateEffects and the kernel's hid-playstation:
    private const byte DualSenseFlag1MicLed = 0x01;
    private const byte DualSenseFlag1Lightbar = 0x04;
    private const byte DualSenseFlag1PlayerLeds = 0x10;
    private const byte DualSenseFlag2LedBrightness = 0x01;

    // DualShock 4 (validFlag0):
    private const byte DualShock4Flag0Lightbar = 0x02;
    private const byte DualShock4Flag0Flash = 0x04;

    /// <summary>
    /// Decodes the light sections of a Sony output report the game wrote
    /// to the virtual pad: the lightbar colour, the DualShock 4 flash, the
    /// DualSense player LEDs, their brightness and the mic-mute LED.
    ///
    /// <para>
    /// Gated on the validity flags exactly like the motors, for the same
    /// reason: a rumble-only report still carries a lightbar field, and
    /// reading that zero as "set the bar black" would blank the light every
    /// time the game vibrated. A report without validity fields is not a
    /// Sony report and carries no lights. Returns false when the report
    /// authorised no light at all.
    /// </para>
    /// </summary>
    public static bool TryDecodeLighting(
        IReadOnlyDictionary<string, object> fields,
        out Effects.GameLighting lighting)
    {
        ArgumentNullException.ThrowIfNull(fields);
        lighting = new Effects.GameLighting();

        var hasValid0 = TryGet(fields, "validFlag0", out var valid0Value);
        var hasValid1 = TryGet(fields, "validFlag1", out var valid1Value);
        var hasValid2 = TryGet(fields, "validFlag2", out var valid2Value);
        if (!hasValid0 && !hasValid1 && !hasValid2)
        {
            return false;
        }

        byte flag0 = 0, flag1 = 0, flag2 = 0;
        if ((hasValid0 && !TryToByte(valid0Value, out flag0))
            || (hasValid1 && !TryToByte(valid1Value, out flag1))
            || (hasValid2 && !TryToByte(valid2Value, out flag2)))
        {
            return false;
        }

        var isDualSense = hasValid2
            || Contains(fields, "leftTriggerEffect")
            || Contains(fields, "rightTriggerEffect")
            || Contains(fields, "playerIndicator");

        if (isDualSense)
        {
            lighting = new Effects.GameLighting(
                Color: (flag1 & DualSenseFlag1Lightbar) != 0 ? ReadColor(fields) : null,
                PlayerIndicator: (flag1 & DualSenseFlag1PlayerLeds) != 0
                    ? ReadByte(fields, "playerIndicator") is { } leds ? (byte)(leds & 0x1F) : null
                    : null,
                MicLed: (flag1 & DualSenseFlag1MicLed) != 0
                    ? ReadByte(fields, "muteLed") is { } mic ? (byte)Math.Min((int)mic, 2) : null
                    : null,
                LedBrightness: (flag2 & DualSenseFlag2LedBrightness) != 0
                    ? ReadByte(fields, "ledBrightness") is { } level ? (byte)Math.Min((int)level, 2) : null
                    : null);
        }
        else
        {
            var flash = (flag0 & DualShock4Flag0Flash) != 0;
            lighting = new Effects.GameLighting(
                Color: (flag0 & DualShock4Flag0Lightbar) != 0 ? ReadColor(fields) : null,
                FlashOn: flash ? ReadByte(fields, "flashOn") : null,
                FlashOff: flash ? ReadByte(fields, "flashOff") : null);
        }

        return lighting.HasAny;
    }

    private static byte? ReadByte(IReadOnlyDictionary<string, object> fields, string name) =>
        TryGet(fields, name, out var value) && TryToByte(value, out var result) ? result : null;

    /// <summary>
    /// Reads the <c>lightbar</c> field. HIDMaestro documents <c>rgb24</c>
    /// as a three-byte array (R, G, B); a memory slice, a byte list and a
    /// packed 0xRRGGBB integer are accepted as well, so an SDK revision
    /// that changes the boxing does not silently drop every colour.
    /// </summary>
    private static GameFlow.Core.Pipeline.LightColor? ReadColor(IReadOnlyDictionary<string, object> fields)
    {
        if (!TryGet(fields, "lightbar", out var value) || value is null)
        {
            return null;
        }

        static GameFlow.Core.Pipeline.LightColor? FromSpan(ReadOnlySpan<byte> bytes) =>
            bytes.Length >= 3 ? new GameFlow.Core.Pipeline.LightColor(bytes[0], bytes[1], bytes[2]) : null;

        switch (value)
        {
            case byte[] array:
                return FromSpan(array);
            case ReadOnlyMemory<byte> memory:
                return FromSpan(memory.Span);
            case Memory<byte> writable:
                return FromSpan(writable.Span);
            case IReadOnlyList<byte> list when list.Count >= 3:
                return new GameFlow.Core.Pipeline.LightColor(list[0], list[1], list[2]);
            case string:
                return null;
        }

        try
        {
            var packed = Convert.ToUInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            return new GameFlow.Core.Pipeline.LightColor(
                (byte)((packed >> 16) & 0xFF), (byte)((packed >> 8) & 0xFF), (byte)(packed & 0xFF));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool Contains(IReadOnlyDictionary<string, object> fields, string name) =>
        fields.ContainsKey(name)
        || fields.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryGet(
        IReadOnlyDictionary<string, object> fields,
        string name,
        out object value)
    {
        if (fields.TryGetValue(name, out value!))
        {
            return true;
        }

        foreach (var pair in fields)
        {
            if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = 0;
        return false;
    }

    private static bool TryToByte(object value, out byte result)
    {
        try
        {
            result = Convert.ToByte(value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception)
        {
            result = 0;
            return false;
        }
    }

    private static double ToUnit(object value)
    {
        try
        {
            var numeric = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
            return double.IsFinite(numeric)
                ? Math.Clamp(numeric, 0d, 255d) / 255d
                : 0d;
        }
        catch (Exception)
        {
            return 0d;
        }
    }
}
