using GameFlow.Core.Models;
using GameFlow.Core.Pipeline;
using Xunit;

namespace GameFlow.Core.Tests;

/// <summary>
/// The layered lighting decision: low-battery alert, then an external
/// request, then the game's own colour, then the configured mode — plus the
/// DualSense player and mic LEDs.
/// </summary>
public sealed class LightingResolutionTests
{
    private static readonly LightColor Red = new(0xFF, 0x00, 0x00);
    private static readonly LightColor Green = new(0x00, 0xFF, 0x00);
    private static readonly LightColor Blue = new(0x00, 0x00, 0xFF);

    private static LightingSettings Solid(string color = "#0000FF") =>
        new() { Mode = LightbarMode.Solid, Color = color };

    [Fact]
    public void WithNothingElseGoingOnTheConfiguredModeIsShown()
    {
        var result = EffectSceneResolver.ResolveLighting(Solid(), new EffectSceneContext(0));

        Assert.Equal(Blue, result.Lightbar);
        Assert.Equal(LightingSource.Configured, result.Source);
    }

    [Fact]
    public void OffIsBlackNotAbsent()
    {
        var result = EffectSceneResolver.ResolveLighting(
            new LightingSettings { Mode = LightbarMode.Off }, new EffectSceneContext(0));

        Assert.Equal(LightColor.Black, result.Lightbar);
    }

    [Fact]
    public void LowBatteryOverridesEverythingWhenTheAlertIsOn()
    {
        var context = new EffectSceneContext(
            0.5, LowBattery: true, External: new ExternalLightRequest(LightbarMode.Solid, Green));

        var result = EffectSceneResolver.ResolveLighting(Solid(), context);

        Assert.Equal(LightingSource.LowBattery, result.Source);
        Assert.True(result.Lightbar.R > result.Lightbar.G);
    }

    [Fact]
    public void TheLowBatteryAlertCanBeTurnedOff()
    {
        var result = EffectSceneResolver.ResolveLighting(
            Solid() with { LowBatteryAlert = false }, new EffectSceneContext(0.5, LowBattery: true));

        Assert.Equal(LightingSource.Configured, result.Source);
        Assert.Equal(Blue, result.Lightbar);
    }

    [Fact]
    public void AnExternalRequestBeatsTheGame()
    {
        var context = new EffectSceneContext(
            0,
            Game: new GameLightState(Red, ColorAgeSeconds: 0),
            External: new ExternalLightRequest(LightbarMode.Solid, Green));

        var result = EffectSceneResolver.ResolveLighting(Solid(), context);

        Assert.Equal(LightingSource.External, result.Source);
        Assert.Equal(Green, result.Lightbar);
    }

    [Fact]
    public void ExternalBrightnessAndTheUserBrightnessBothApply()
    {
        var context = new EffectSceneContext(0, External: new ExternalLightRequest(LightbarMode.Solid, Green, 0.5f));

        var result = EffectSceneResolver.ResolveLighting(Solid() with { Brightness = 0.5f }, context);

        Assert.InRange(result.Lightbar.G, 60, 68);
    }

    [Fact]
    public void ExternalOffTurnsTheLightOff()
    {
        var context = new EffectSceneContext(0, External: new ExternalLightRequest(LightbarMode.Off, Green));

        Assert.Equal(LightColor.Black, EffectSceneResolver.ResolveLighting(Solid(), context).Lightbar);
    }

    [Fact]
    public void ARecentGameColourWinsWhileFollowingTheGame()
    {
        var context = new EffectSceneContext(0, Game: new GameLightState(Red, ColorAgeSeconds: 2));

        var result = EffectSceneResolver.ResolveLighting(Solid(), context);

        Assert.Equal(LightingSource.Game, result.Source);
        Assert.Equal(Red, result.Lightbar);
    }

    [Fact]
    public void AStaleGameColourHandsTheBarBack()
    {
        var context = new EffectSceneContext(0, Game: new GameLightState(Red, ColorAgeSeconds: 30));

        var result = EffectSceneResolver.ResolveLighting(Solid(), context);

        Assert.Equal(LightingSource.Configured, result.Source);
    }

    [Fact]
    public void ZeroIdleFallbackHoldsTheGameColourIndefinitely()
    {
        var context = new EffectSceneContext(0, Game: new GameLightState(Red, ColorAgeSeconds: 3600));

        var result = EffectSceneResolver.ResolveLighting(Solid() with { GameIdleFallbackSeconds = 0 }, context);

        Assert.Equal(LightingSource.Game, result.Source);
    }

    [Fact]
    public void FollowGameOffIgnoresTheGame()
    {
        var context = new EffectSceneContext(0, Game: new GameLightState(Red, ColorAgeSeconds: 0));

        var result = EffectSceneResolver.ResolveLighting(Solid() with { FollowGame = false }, context);

        Assert.Equal(Blue, result.Lightbar);
    }

    [Fact]
    public void GameControlledKeepsAnOldGameColour()
    {
        var context = new EffectSceneContext(0, Game: new GameLightState(Red, ColorAgeSeconds: 3600));

        var result = EffectSceneResolver.ResolveLighting(
            new LightingSettings { Mode = LightbarMode.GameControlled, FollowGame = false }, context);

        Assert.Equal(Red, result.Lightbar);
    }

    [Fact]
    public void GameControlledShowsTheSlotColourUntilTheGameSendsOne()
    {
        var waiting = EffectSceneResolver.ResolveLighting(
            new LightingSettings { Mode = LightbarMode.GameControlled }, new EffectSceneContext(0, PlayerIndex: 1));
        var player = EffectSceneResolver.ResolveLighting(
            new LightingSettings { Mode = LightbarMode.PlayerNumber }, new EffectSceneContext(0, PlayerIndex: 1));

        Assert.Equal(player.Lightbar, waiting.Lightbar);
    }

    [Fact]
    public void ExternalModeIgnoresTheGameAndShowsTheColourWhileIdle()
    {
        var context = new EffectSceneContext(0, Game: new GameLightState(Red, ColorAgeSeconds: 0));

        var result = EffectSceneResolver.ResolveLighting(
            new LightingSettings { Mode = LightbarMode.External, Color = "#00FF00" }, context);

        Assert.Equal(LightingSource.Configured, result.Source);
        Assert.Equal(Green, result.Lightbar);
    }

    [Fact]
    public void ADualShock4FlashBlinksTheGameColour()
    {
        var on = 0;
        var samples = 0;
        for (var t = 0.0; t < 3.0; t += 0.01)
        {
            var game = new GameLightState(Red, 0, FlashOnSeconds: 0.25, FlashOffSeconds: 0.25);
            var bar = EffectSceneResolver.ResolveLighting(Solid(), new EffectSceneContext(t, Game: game)).Lightbar;
            if (bar == Red) { on++; }
            samples++;
        }

        Assert.InRange((double)on / samples, 0.4, 0.6);
    }

    [Fact]
    public void StrobeAlternatesWithTheSecondaryColour()
    {
        var settings = new LightingSettings { Mode = LightbarMode.Strobe, Color = "#FF0000", SecondaryColor = "#00FF00" };

        var seen = new HashSet<LightColor>();
        for (var t = 0.0; t < 1.0; t += 0.01)
        {
            seen.Add(EffectSceneResolver.ResolveLight(settings, new EffectSceneContext(t)));
        }

        Assert.Equal(new[] { Red, Green }, seen.OrderByDescending(c => c.R).ToArray());
    }

    [Fact]
    public void TriggerReactiveUsesTheSecondaryColourWhenSet()
    {
        var held = EffectSceneResolver.ResolveLight(
            new LightingSettings { Mode = LightbarMode.TriggerReactive, SecondaryColor = "#00FF00" },
            new EffectSceneContext(0, TriggerLevel: 1));

        Assert.Equal(Green, held);
    }

    [Fact]
    public void AnimationSpeedChangesHowFastTheRainbowTurns()
    {
        var normal = new LightingSettings { Mode = LightbarMode.Rainbow };
        var fast = normal with { AnimationSpeed = 2f };

        // At double speed, one second in is where normal speed is at two.
        Assert.Equal(
            EffectSceneResolver.ResolveLight(normal, new EffectSceneContext(2)),
            EffectSceneResolver.ResolveLight(fast, new EffectSceneContext(1)));
    }

    [Fact]
    public void PlayerLedsShowTheSlotNumberByDefault()
    {
        var first = EffectSceneResolver.ResolveLighting(new LightingSettings(), new EffectSceneContext(0, PlayerIndex: 0));
        var second = EffectSceneResolver.ResolveLighting(new LightingSettings(), new EffectSceneContext(0, PlayerIndex: 1));

        Assert.Equal(0x04, first.PlayerLeds);
        Assert.Equal(0x0A, second.PlayerLeds);
    }

    [Theory]
    [InlineData(100, 0x1F)]
    [InlineData(50, 0x07)]
    [InlineData(3, 0x01)]
    public void BatteryModeFillsTheLedRowFromTheLeft(int percent, byte expected)
    {
        var result = EffectSceneResolver.ResolveLighting(
            new LightingSettings { PlayerLedMode = PlayerLedMode.Battery },
            new EffectSceneContext(0, BatteryPercent: percent));

        Assert.Equal(expected, result.PlayerLeds);
    }

    [Fact]
    public void CustomAndGamePlayerLedModes()
    {
        var custom = EffectSceneResolver.ResolveLighting(
            new LightingSettings { PlayerLedMode = PlayerLedMode.Custom, PlayerLedMask = 0x11 }, new EffectSceneContext(0));
        var game = EffectSceneResolver.ResolveLighting(
            new LightingSettings { PlayerLedMode = PlayerLedMode.Game },
            new EffectSceneContext(0, Game: new GameLightState(null, PlayerLeds: 0x1B, PlayerLedBrightness: IndicatorBrightnessLevel.Dim)));

        Assert.Equal(0x11, custom.PlayerLeds);
        Assert.Equal(0x1B, game.PlayerLeds);
        Assert.Equal(IndicatorBrightnessLevel.Dim, game.PlayerLedBrightness);
    }

    [Theory]
    [InlineData(1.0f, IndicatorBrightnessLevel.Bright)]
    [InlineData(0.5f, IndicatorBrightnessLevel.Medium)]
    [InlineData(0.2f, IndicatorBrightnessLevel.Dim)]
    public void TheIndicatorSliderMapsToTheThreeFirmwareSteps(float slider, IndicatorBrightnessLevel expected)
    {
        var result = EffectSceneResolver.ResolveLighting(
            new LightingSettings { IndicatorBrightness = slider }, new EffectSceneContext(0));

        Assert.Equal(expected, result.PlayerLedBrightness);
        Assert.NotEqual(0, result.PlayerLeds);
    }

    [Fact]
    public void TheIndicatorSliderAtZeroSwitchesTheRowOff()
    {
        var result = EffectSceneResolver.ResolveLighting(
            new LightingSettings { IndicatorBrightness = 0f }, new EffectSceneContext(0));

        Assert.Equal(0, result.PlayerLeds);
    }

    [Theory]
    [InlineData(MicLedMode.Off, MicLedState.Off)]
    [InlineData(MicLedMode.On, MicLedState.On)]
    [InlineData(MicLedMode.Pulse, MicLedState.Pulse)]
    public void MicLedFollowsItsMode(MicLedMode mode, MicLedState expected)
    {
        Assert.Equal(
            expected,
            EffectSceneResolver.ResolveLighting(new LightingSettings { MicLedMode = mode }, new EffectSceneContext(0)).MicLed);
    }

    [Fact]
    public void MicLedInGameModeFollowsTheGame()
    {
        var settings = new LightingSettings { MicLedMode = MicLedMode.Game };

        Assert.Equal(MicLedState.Off, EffectSceneResolver.ResolveLighting(settings, new EffectSceneContext(0)).MicLed);
        Assert.Equal(
            MicLedState.On,
            EffectSceneResolver.ResolveLighting(
                settings, new EffectSceneContext(0, Game: new GameLightState(null, MicLed: MicLedState.On))).MicLed);
    }

    [Theory]
    [InlineData(0x00, -1)]
    [InlineData(0x04, 0)]
    [InlineData(0x0A, 1)]
    [InlineData(0x1F, 4)]
    [InlineData(0x03, 1)] // not a console pattern: two LEDs lit, player 2
    public void PatternsMapToTheNearestSdlPlayerNumber(byte pattern, int expected)
    {
        Assert.Equal(expected, EffectSceneResolver.ToPlayerIndex(pattern));
    }
}
