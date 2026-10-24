using GameFlow.Core.Pipeline;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

/// <summary>
/// Decoding the lights a game wrote to the virtual DualSense / DualShock 4.
/// Field names and validity bits follow HIDMaestro 2.0.1's profiles
/// (<c>lightbar</c> is rgb24 → byte[3]) and SDL's / the kernel's DS4 and
/// DS5 flag layouts.
/// </summary>
public sealed class HidMaestroLightingDecoderTests
{
    private static Dictionary<string, object> DualSense(byte flag0, byte flag1, byte flag2) => new()
    {
        ["validFlag0"] = flag0,
        ["validFlag1"] = flag1,
        ["validFlag2"] = flag2,
        ["leftMotor"] = (byte)0,
        ["rightMotor"] = (byte)0,
        ["muteLed"] = (byte)1,
        ["ledBrightness"] = (byte)2,
        ["playerIndicator"] = (byte)0x2A, // bit 5 is the firmware's "instant" flag
        ["lightbar"] = new byte[] { 0xFF, 0x10, 0x00 },
        ["leftTriggerEffect"] = new byte[11],
    };

    private static Dictionary<string, object> DualShock4(byte flag0) => new()
    {
        ["validFlag0"] = flag0,
        ["validFlag1"] = (byte)0,
        ["leftMotor"] = (byte)0,
        ["rightMotor"] = (byte)0,
        ["lightbar"] = new byte[] { 0x00, 0x00, 0xFF },
        ["flashOn"] = (byte)20,
        ["flashOff"] = (byte)30,
    };

    [Fact]
    public void DualSenseLightbarIsReadWhenItsFlagIsSet()
    {
        Assert.True(HidMaestroRumbleDecoder.TryDecodeLighting(DualSense(0, 0x04, 0), out var lighting));

        Assert.Equal(new LightColor(0xFF, 0x10, 0x00), lighting.Color);
        Assert.Null(lighting.PlayerIndicator);
        Assert.Null(lighting.MicLed);
    }

    [Fact]
    public void DualSenseIndicatorsAreReadOnlyWithTheirOwnFlags()
    {
        Assert.True(HidMaestroRumbleDecoder.TryDecodeLighting(DualSense(0, 0x10 | 0x01, 0x01), out var lighting));

        Assert.Null(lighting.Color);
        Assert.Equal((byte)0x0A, lighting.PlayerIndicator);
        Assert.Equal((byte)1, lighting.MicLed);
        Assert.Equal((byte)2, lighting.LedBrightness);
    }

    [Fact]
    public void ARumbleOnlyReportCarriesNoLights()
    {
        // The lightbar field is present in every report; without its flag it
        // is padding, and reading it would blank the bar on every rumble.
        Assert.False(HidMaestroRumbleDecoder.TryDecodeLighting(DualSense(0x03, 0, 0), out _));
    }

    [Fact]
    public void DualShock4LightbarAndFlash()
    {
        Assert.True(HidMaestroRumbleDecoder.TryDecodeLighting(DualShock4(0x02 | 0x04), out var lighting));

        Assert.Equal(new LightColor(0, 0, 0xFF), lighting.Color);
        Assert.Equal((byte)20, lighting.FlashOn);
        Assert.Equal((byte)30, lighting.FlashOff);
    }

    [Fact]
    public void DualShock4MotorOnlyReportCarriesNoLights()
    {
        Assert.False(HidMaestroRumbleDecoder.TryDecodeLighting(DualShock4(0x01), out _));
    }

    [Fact]
    public void FieldsWithoutValidityFlagsAreNotASonyReport()
    {
        var fields = new Dictionary<string, object> { ["lightbar"] = new byte[] { 1, 2, 3 } };

        Assert.False(HidMaestroRumbleDecoder.TryDecodeLighting(fields, out _));
    }

    [Theory]
    [MemberData(nameof(ColourShapes))]
    public void TheColourIsReadFromEveryPlausibleBoxing(object value)
    {
        var fields = DualSense(0, 0x04, 0);
        fields["lightbar"] = value;

        Assert.True(HidMaestroRumbleDecoder.TryDecodeLighting(fields, out var lighting));
        Assert.Equal(new LightColor(0x12, 0x34, 0x56), lighting.Color);
    }

    public static TheoryData<object> ColourShapes() => new()
    {
        new byte[] { 0x12, 0x34, 0x56 },
        new ReadOnlyMemory<byte>([0x12, 0x34, 0x56]),
        new List<byte> { 0x12, 0x34, 0x56 },
        0x123456,
    };
}
