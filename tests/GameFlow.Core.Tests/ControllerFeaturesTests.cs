using GameFlow.Core.Models;
using Xunit;

namespace GameFlow.Core.Tests;

/// <summary>
/// The per-family capability table and the rules for combining it with
/// what SDL detects. These decide which tuning sections a pad gets, so a
/// wrong bit either hides a real feature or offers a dead control.
/// </summary>
public sealed class ControllerFeaturesTests
{
    [Theory]
    [InlineData(0x0CE6)] // DualSense
    [InlineData(0x0DF2)] // DualSense Edge
    public void DualSenseFamilyHasEveryDualSenseOnlyOutput(ushort pid)
    {
        var features = ControllerHardwareCatalog.DefaultFeatures(0x054C, pid);

        Assert.True(features.HasFlag(ControllerFeatures.RgbLight));
        Assert.True(features.HasFlag(ControllerFeatures.PlayerLeds));
        Assert.True(features.HasFlag(ControllerFeatures.MicLed));
        Assert.True(features.HasFlag(ControllerFeatures.AdaptiveTriggers));
        Assert.True(features.HasFlag(ControllerFeatures.Rumble));
        Assert.False(features.HasFlag(ControllerFeatures.TriggerRumble));
    }

    [Fact]
    public void DualShock4HasALightbarWithFlashButNoPlayerOrMicLeds()
    {
        var features = ControllerHardwareCatalog.DefaultFeatures(0x054C, 0x09CC);

        Assert.True(features.HasFlag(ControllerFeatures.RgbLight));
        Assert.True(features.HasFlag(ControllerFeatures.LightbarFlash));
        Assert.False(features.HasFlag(ControllerFeatures.PlayerLeds));
        Assert.False(features.HasFlag(ControllerFeatures.MicLed));
        Assert.False(features.HasFlag(ControllerFeatures.AdaptiveTriggers));
    }

    [Fact]
    public void DualShock3HasPlayerLedsAndNoLightbar()
    {
        var features = ControllerHardwareCatalog.DefaultFeatures(0x054C, 0x0268);

        Assert.True(features.HasFlag(ControllerFeatures.PlayerLeds));
        Assert.False(features.HasAny(ControllerFeatureSet.AnyMainLight));
    }

    [Theory]
    [InlineData(0x02EA)] // Xbox One S
    [InlineData(0x0B12)] // Series X|S
    public void ModernXboxPadsHaveImpulseTriggersAndNoLights(ushort pid)
    {
        var features = ControllerHardwareCatalog.DefaultFeatures(0x045E, pid);

        Assert.True(features.HasFlag(ControllerFeatures.TriggerRumble));
        Assert.False(features.HasAny(ControllerFeatureSet.AnyLighting));
    }

    [Fact]
    public void Xbox360HasNoImpulseTriggers()
    {
        Assert.False(ControllerHardwareCatalog.DefaultFeatures(0x045E, 0x028E).HasFlag(ControllerFeatures.TriggerRumble));
    }

    [Fact]
    public void SwitchProHasPlayerLedsAndTheHomeLight()
    {
        var features = ControllerHardwareCatalog.DefaultFeatures(0x057E, 0x2009);

        Assert.True(features.HasFlag(ControllerFeatures.PlayerLeds));
        Assert.True(features.HasFlag(ControllerFeatures.MonoLight));
        Assert.False(features.HasFlag(ControllerFeatures.RgbLight));
    }

    [Fact]
    public void SteamDeckHasRumbleButNoLights()
    {
        var features = ControllerHardwareCatalog.DefaultFeatures(0x28DE, 0x1205);

        Assert.True(features.HasFlag(ControllerFeatures.Rumble));
        Assert.False(features.HasAny(ControllerFeatureSet.AnyLighting));
    }

    [Fact]
    public void AnUnrecognisedPadIsAssumedToRumbleOnly()
    {
        Assert.Equal(ControllerFeatures.Rumble, ControllerHardwareCatalog.DefaultFeatures(0x1234, 0x5678));
    }

    [Fact]
    public void NoIdentityMeansUnknownNotNothing()
    {
        Assert.Equal(ControllerFeatures.None, ControllerHardwareCatalog.DefaultFeatures(0, 0));
    }

    [Fact]
    public void DetectionDecidesTheBitsSdlCanSee()
    {
        // SDL found no lightbar on this DualShock 4 (a driver that does not
        // expose it): the detected answer wins over the table.
        var merged = ControllerHardwareCatalog.MergeDetected(ControllerFeatures.Rumble, 0x054C, 0x09CC);

        Assert.False(merged.HasFlag(ControllerFeatures.RgbLight));
        Assert.True(merged.HasFlag(ControllerFeatures.Rumble));
    }

    [Fact]
    public void TheCatalogFillsTheBitsSdlHasNoPropertyFor()
    {
        var merged = ControllerHardwareCatalog.MergeDetected(
            ControllerFeatures.Rumble | ControllerFeatures.RgbLight | ControllerFeatures.PlayerLeds, 0x054C, 0x0CE6);

        Assert.True(merged.HasFlag(ControllerFeatures.MicLed));
        Assert.True(merged.HasFlag(ControllerFeatures.Battery));
        Assert.True(merged.HasFlag(ControllerFeatures.RgbLight));
    }

    [Fact]
    public void NothingDetectedReturnsTheCatalogUnchanged()
    {
        Assert.Equal(
            ControllerHardwareCatalog.DefaultFeatures(0x054C, 0x0CE6),
            ControllerHardwareCatalog.MergeDetected(ControllerFeatures.None, 0x054C, 0x0CE6));
    }

    [Theory]
    [InlineData(0x054C, 0x0DF2, "DualSense Edge")]
    [InlineData(0x054C, 0x0CE6, "DualSense")]
    [InlineData(0x054C, 0x05C4, "DualShock 4")]
    [InlineData(0x045E, 0x0B12, "Xbox Series")]
    [InlineData(0x057E, 0x2009, "Switch Pro Controller")]
    public void FamilyNamesAreTheOnesPeopleUse(ushort vid, ushort pid, string expected)
    {
        Assert.Equal(expected, ControllerHardwareCatalog.FamilyName(vid, pid));
    }

    [Fact]
    public void AnUnknownPadHasNoInventedFamilyName()
    {
        Assert.Null(ControllerHardwareCatalog.FamilyName(0x1234, 0x5678));
    }

    [Fact]
    public void UnknownFeaturesAllowEverySectionButHaveNone()
    {
        Assert.True(ControllerFeatures.None.Allows(ControllerFeatures.AdaptiveTriggers));
        Assert.False(ControllerFeatures.None.HasAny(ControllerFeatures.AdaptiveTriggers));
        Assert.False(ControllerFeatures.Rumble.Allows(ControllerFeatures.AdaptiveTriggers));
    }

    [Fact]
    public void DescribeListsFeaturesInAStableReadableOrder()
    {
        var names = ControllerFeatureSet.Describe(
            ControllerFeatures.MicLed | ControllerFeatures.Rumble | ControllerFeatures.RgbLight);

        Assert.Equal(new[] { "rumble", "colour lightbar", "mic-mute LED" }, names.ToArray());
        Assert.Empty(ControllerFeatureSet.Describe(ControllerFeatures.None));
    }
}
