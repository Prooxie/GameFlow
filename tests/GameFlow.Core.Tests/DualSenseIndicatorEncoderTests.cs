using GameFlow.Core.Pipeline;
using Xunit;

namespace GameFlow.Core.Tests;

/// <summary>
/// The player-LED, LED-brightness and mic-LED sections of the DualSense
/// report. Offsets and enable bits follow SDL's DS5EffectsState_t and the
/// kernel's hid-playstation; a payload with the wrong enable bit is ignored
/// by the firmware without any error, so the bits are pinned here.
/// </summary>
public sealed class DualSenseIndicatorEncoderTests
{
    private static byte[] Write(
        byte? playerLeds = null,
        IndicatorBrightnessLevel? brightness = null,
        MicLedState? mic = null)
    {
        var report = new byte[DualSenseEffectEncoder.EffectStateLength];
        Assert.True(DualSenseEffectEncoder.TryWrite(
            report, null, null, null, 0, 0, DualSenseRumbleMode.Enhanced, playerLeds, brightness, mic));
        return report;
    }

    [Fact]
    public void PlayerLedsGoToByte43WithTheirEnableBitAndTheInstantFlag()
    {
        var report = Write(playerLeds: 0x0A);

        Assert.Equal(0x10, report[1] & 0x10);
        Assert.Equal(0x0A | 0x20, report[43]);
    }

    [Fact]
    public void AllPlayerLedsOffIsStillAnExplicitWrite()
    {
        var report = Write(playerLeds: 0);

        Assert.Equal(0x10, report[1] & 0x10);
        Assert.Equal(0x20, report[43]);
    }

    [Fact]
    public void BrightnessGoesToByte42WithItsEnableBitInTheThirdFlagByte()
    {
        var report = Write(brightness: IndicatorBrightnessLevel.Dim);

        Assert.Equal(0x01, report[38] & 0x01);
        Assert.Equal(2, report[42]);
    }

    [Fact]
    public void MicLedGoesToByte8WithItsEnableBit()
    {
        var report = Write(mic: MicLedState.Pulse);

        Assert.Equal(0x01, report[1] & 0x01);
        Assert.Equal(2, report[8]);
    }

    [Fact]
    public void AbsentIndicatorsLeaveTheirSectionsDisabled()
    {
        var report = Write();

        Assert.Equal(0, report[1]);
        Assert.Equal(0, report[38]);
        Assert.Equal(0, report[43]);
        Assert.Equal(0, report[8]);
    }

    [Fact]
    public void IndicatorsDoNotDisturbTheLightbarOrEnhancedRumble()
    {
        var report = new byte[DualSenseEffectEncoder.EffectStateLength];
        Assert.True(DualSenseEffectEncoder.TryWrite(
            report, null, null, new LightColor(1, 2, 3), 0xFFFF, 0, DualSenseRumbleMode.Enhanced,
            0x04, IndicatorBrightnessLevel.Medium, MicLedState.On));

        Assert.Equal(0x04 | 0x10 | 0x01, report[1]);
        Assert.Equal(0x04 | 0x01, report[38]);
        Assert.Equal(new byte[] { 1, 2, 3 }, report[44..47]);
        Assert.Equal(0xFF, report[3]);
    }
}
