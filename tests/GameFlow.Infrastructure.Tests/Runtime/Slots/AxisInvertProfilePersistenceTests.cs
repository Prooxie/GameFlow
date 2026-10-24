using System.Text.Json;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using GameFlow.Core.Pipeline;
using GameFlow.Infrastructure.Profiles;
using GameFlow.Infrastructure.Runtime.Slots;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Slots;

/// <summary>
/// Axis inversion is an <see cref="AxisInvertRule"/> in the profile's rule
/// list, so it has to survive the JSON round trip like any rule — and,
/// because every axis is nullable to make layering work, "never set" has to
/// stay distinguishable from "explicitly off" all the way to disk and back.
/// Profiles from before v7 kept it in a header field; those must come back
/// as exactly one rule that means the same thing.
/// </summary>
public sealed class AxisInvertProfilePersistenceTests
{
    private static ProfileDocument RoundTrip(ProfileDocument profile)
    {
        var json = JsonSerializer.Serialize(profile, ProfileJsonOptions.Default);
        return JsonSerializer.Deserialize<ProfileDocument>(json, ProfileJsonOptions.Default)!;
    }

    private static ControllerSnapshot PushedUpAndRight() => new()
    {
        LeftStick = new StickVector(0.4f, 0.8f),
        RightStick = new StickVector(0.6f, 0.5f)
    };

    [Fact]
    public void InversionRuleSurvivesTheProfileRoundTrip()
    {
        var profile = new ProfileDocument
        {
            Rules = [new AxisInvertRule { Id = "inv", Axes = new AxisInvert { RightY = true, LeftX = false, ShowInLayout = true } }]
        };

        var restored = Assert.IsType<AxisInvertRule>(Assert.Single(RoundTrip(profile).Rules));

        Assert.True(restored.Axes.RightY);
        Assert.False(restored.Axes.LeftX);
        Assert.True(restored.Axes.ShowInLayout);
        Assert.Null(restored.Axes.LeftY);
    }

    [Fact]
    public void UnsetAxesStayUnset_AndTheLegacyHeaderIsNotWritten()
    {
        // The distinction the whole nullable design rests on: an axis
        // nobody touched must not come back as an explicit "off", or a
        // rule folded on top of another would silently cancel it.
        var profile = new ProfileDocument
        {
            Rules = [new AxisInvertRule { Id = "inv", Axes = new AxisInvert { RightY = true } }]
        };

        var json = JsonSerializer.Serialize(profile, ProfileJsonOptions.Default);

        Assert.Contains("\"axis-invert\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("leftX", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"invert\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyHeaderInversion_BecomesOneRule_WithEveryAxisVerbatim()
    {
        const string v6Json = """
        {
          "id": "racing",
          "name": "Racing",
          "version": 6,
          "pollingRateHz": 1000,
          "inputProvider": "sdl",
          "outputProvider": "preview",
          "rules": [],
          "invert": { "rightY": true, "leftX": false, "showInLayout": true }
        }
        """;

        var loaded = JsonSerializer.Deserialize<ProfileDocument>(v6Json, ProfileJsonOptions.Default)!;
        var migrated = ProfileMigrator.Migrate(loaded);

        var rule = Assert.IsType<AxisInvertRule>(Assert.Single(migrated.Rules));
        Assert.Equal("axis-invert-racing", rule.Id);
        Assert.Equal(ProfileMigrator.LegacyAxisInvertRuleName, rule.Name);
        Assert.True(rule.Enabled);
        Assert.Equal(string.Empty, rule.LayerId);
        Assert.True(rule.Axes.RightY);
        Assert.False(rule.Axes.LeftX);   // explicit false kept: it switches a lower layer's flip off
        Assert.Null(rule.Axes.LeftY);    // never set stays never set
        Assert.True(rule.Axes.ShowInLayout);
        Assert.Null(migrated.Invert);
        Assert.Equal(ProfileMigrator.CurrentVersion, migrated.Version);
    }

    [Fact]
    public void ProfileWrittenBeforeInversionExisted_LoadsWithNothingInverted()
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

        var migrated = ProfileMigrator.Migrate(JsonSerializer.Deserialize<ProfileDocument>(legacyJson, ProfileJsonOptions.Default)!);

        Assert.Empty(migrated.Rules);
        Assert.Null(migrated.Invert);
    }

    [Fact]
    public void ComposedSlotProfile_LetsALaterLayerOverrideAnEarlierOne_AxisByAxis()
    {
        // Two v6 profiles, each with its own header inversion, stacked on a
        // slot: exactly what the old composer folded. After migration each
        // carries its own rule (per-profile ids, so neither replaces the
        // other) and the pipeline folds them in stack order.
        var baseLayer = ProfileMigrator.Migrate(new ProfileDocument
        {
            Id = "base",
            Name = "Base",
            Invert = new AxisInvert { RightY = true, LeftX = true }
        });
        var topLayer = ProfileMigrator.Migrate(new ProfileDocument
        {
            Id = "top",
            Name = "Top",
            Invert = new AxisInvert { RightY = false }
        });

        var composed = SlotProfileComposer.Compose([baseLayer, topLayer]);
        Assert.Equal(2, composed.Rules.OfType<AxisInvertRule>().Count());

        using var pipeline = new ControllerMappingPipeline(composed);
        var output = pipeline.Process(PushedUpAndRight(), DateTimeOffset.UtcNow).OutputSnapshot;

        Assert.Equal(0.5f, output.RightStick.Y, precision: 5);   // top switched it back off
        Assert.Equal(-0.4f, output.LeftStick.X, precision: 5);   // top had no opinion: base applies
    }

    [Fact]
    public void ComposedSlotProfile_LaterLayersRuleWithTheSameId_ReplacesTheEarlierOne()
    {
        // Profiles created from the defaults share rule ids. Editing the
        // shared rule in the TOP profile has to take effect; first-wins kept
        // the bottom profile's copy and threw the edit away.
        var baseLayer = new ProfileDocument
        {
            Id = "base",
            Rules =
            [
                new ButtonRemapRule { Id = "shared", Name = "old", SourceButton = ButtonId.South, TargetButton = ButtonId.West },
                new ButtonRemapRule { Id = "base-only", SourceButton = ButtonId.East, TargetButton = ButtonId.North }
            ]
        };
        var topLayer = new ProfileDocument
        {
            Id = "top",
            Rules = [new ButtonRemapRule { Id = "shared", Name = "new", SourceButton = ButtonId.South, TargetButton = ButtonId.North }]
        };

        var composed = SlotProfileComposer.Compose([baseLayer, topLayer]);

        Assert.Equal(["base-only", "shared"], composed.Rules.Select(r => r.Id));
        Assert.Equal("new", composed.Rules.Single(r => r.Id == "shared").Name);
    }

    [Fact]
    public void ComposedSlotProfile_KeepsTheOnlyLayersInversion()
    {
        var only = new ProfileDocument
        {
            Id = "only",
            Name = "Only",
            Rules = [new AxisInvertRule { Id = "inv", Axes = new AxisInvert { LeftY = true } }]
        };

        Assert.True(Assert.IsType<AxisInvertRule>(Assert.Single(SlotProfileComposer.Compose([only]).Rules)).Axes.LeftY);
    }
}
