using System.Text.Json;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Profiles;
using GameFlow.Infrastructure.Runtime.Slots;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Slots;

/// <summary>
/// Per-profile axis inversion is written into the profile document, so
/// it has to survive that JSON round trip — and, because every axis is
/// nullable to make profile layering work, "never set" has to stay
/// distinguishable from "explicitly off" all the way to disk and back.
/// </summary>
public sealed class AxisInvertProfilePersistenceTests
{
    private static ProfileDocument RoundTrip(ProfileDocument profile)
    {
        var json = JsonSerializer.Serialize(profile, ProfileJsonOptions.Default);
        return JsonSerializer.Deserialize<ProfileDocument>(json, ProfileJsonOptions.Default)!;
    }

    [Fact]
    public void InversionSurvivesTheProfileRoundTrip()
    {
        var profile = new ProfileDocument
        {
            Invert = new AxisInvert { RightY = true, LeftX = false, ShowInLayout = true }
        };

        var restored = RoundTrip(profile);

        Assert.True(restored.Invert.RightY);
        Assert.False(restored.Invert.LeftX);
        Assert.True(restored.Invert.ShowInLayout);
    }

    [Fact]
    public void UnsetAxesStayUnset_NotWrittenAsOff()
    {
        // The distinction the whole nullable design rests on: an axis
        // nobody touched must not come back as an explicit "off", or a
        // profile layered on top of another would silently cancel the
        // one underneath it.
        var profile = new ProfileDocument { Invert = new AxisInvert { RightY = true } };

        var json = JsonSerializer.Serialize(profile, ProfileJsonOptions.Default);
        var restored = RoundTrip(profile);

        Assert.DoesNotContain("leftX", json, StringComparison.Ordinal);
        Assert.Null(restored.Invert.LeftX);
        Assert.Null(restored.Invert.LeftTrigger);
    }

    [Fact]
    public void ProfileWrittenBeforeThisFeature_LoadsWithNothingInverted()
    {
        const string legacyJson = """
        {
          "id": "legacy",
          "name": "Legacy",
          "version": 5,
          "pollingRateHz": 1000,
          "inputProvider": "sdl",
          "outputProvider": "preview",
          "rules": []
        }
        """;

        var restored = JsonSerializer.Deserialize<ProfileDocument>(legacyJson, ProfileJsonOptions.Default)!;

        Assert.NotNull(restored.Invert);
        Assert.True(restored.Invert.IsIdentity);
        Assert.Null(restored.Invert.RightY);
    }

    [Fact]
    public void ComposedSlotProfile_LetsALaterLayerOverrideAnEarlierOne()
    {
        var baseLayer = new ProfileDocument
        {
            Id = "base",
            Name = "Base",
            Invert = new AxisInvert { RightY = true, LeftX = true }
        };
        var topLayer = new ProfileDocument
        {
            Id = "top",
            Name = "Top",
            Invert = new AxisInvert { RightY = false }
        };

        var composed = SlotProfileComposer.Compose([baseLayer, topLayer]);

        Assert.False(composed.Invert.RightY);
        Assert.True(composed.Invert.LeftX);
    }

    [Fact]
    public void ComposedSlotProfile_KeepsTheOnlyLayersInversion()
    {
        var only = new ProfileDocument
        {
            Id = "only",
            Name = "Only",
            Invert = new AxisInvert { LeftY = true }
        };

        Assert.True(SlotProfileComposer.Compose([only]).Invert.LeftY);
    }
}
