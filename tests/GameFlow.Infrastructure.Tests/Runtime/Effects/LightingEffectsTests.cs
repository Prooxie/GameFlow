using GameFlow.Core.Models;
using GameFlow.Core.Pipeline;
using GameFlow.Infrastructure.Localization;
using GameFlow.Infrastructure.Runtime;
using GameFlow.Infrastructure.Runtime.Effects;
using GameFlow.Infrastructure.Runtime.Slots;
using GameFlow.Infrastructure.Runtime.Templates;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Effects;

/// <summary>
/// The Infrastructure half of per-controller lighting: which lights a
/// state carries for which hardware, how the queue paces light-only
/// changes, how the DS5 report is chosen, and the two request stores.
/// </summary>
public sealed class LightingEffectsTests
{
    private static readonly ResolvedLighting Lit = new(
        new LightColor(1, 2, 3), LightingSource.Configured, 0x04, IndicatorBrightnessLevel.Medium, MicLedState.On);

    private static ControllerEffectState Build(ControllerFeatures features) =>
        ControllerEffectProducer.BuildState(
            features, 0, 0, 0, 0, Lit, AdaptiveTriggerCommand.Release, AdaptiveTriggerCommand.Release);

    [Fact]
    public void ADualSenseStateCarriesEveryLight()
    {
        var state = Build(ControllerHardwareCatalog.DefaultFeatures(0x054C, 0x0CE6));

        Assert.Equal(new EffectColor(1, 2, 3), state.LedColor);
        Assert.Equal((byte)0x04, state.PlayerLeds);
        Assert.Equal(IndicatorBrightnessLevel.Medium, state.PlayerLedBrightness);
        Assert.Equal(MicLedState.On, state.MicLed);
    }

    [Fact]
    public void AnXboxStateCarriesNoLightsSoAnimationsCauseNoWrites()
    {
        var state = Build(ControllerHardwareCatalog.DefaultFeatures(0x045E, 0x0B12));

        Assert.Null(state.LedColor);
        Assert.Null(state.PlayerLeds);
        Assert.Null(state.MicLed);
    }

    [Fact]
    public void ADualShock3GetsPlayerLedsButNoDualSenseOnlyBrightness()
    {
        var state = Build(ControllerHardwareCatalog.DefaultFeatures(0x054C, 0x0268));

        Assert.Equal((byte)0x04, state.PlayerLeds);
        Assert.Null(state.PlayerLedBrightness);
        Assert.Null(state.LedColor);
    }

    [Fact]
    public void AnUnknownPadStillGetsTheColourButNoIndicators()
    {
        var state = Build(ControllerFeatures.None);

        Assert.NotNull(state.LedColor);
        Assert.Null(state.PlayerLeds);
        Assert.Null(state.MicLed);
    }

    [Fact]
    public void TriggerRumbleMirrorsTheMotorsOnlyWhenAskedAndSupported()
    {
        var xbox = ControllerHardwareCatalog.DefaultFeatures(0x045E, 0x0B12);
        var mirrored = new RumbleSettings { TriggerRumbleSource = TriggerRumbleSource.Motors, TriggerRumbleGain = 0.5f };

        Assert.Equal((0.4, 0.2), ControllerEffectProducer.ResolveTriggerRumble(mirrored, xbox, 0.8, 0.4));
        Assert.Equal((0d, 0d), ControllerEffectProducer.ResolveTriggerRumble(new RumbleSettings(), xbox, 0.8, 0.4));
        Assert.Equal(
            (0d, 0d),
            ControllerEffectProducer.ResolveTriggerRumble(mirrored, ControllerHardwareCatalog.DefaultFeatures(0x054C, 0x0CE6), 0.8, 0.4));
    }

    [Fact]
    public void ALightOnlyChangeWaitsForTheLongerLightingInterval()
    {
        var queue = new EffectDispatchQueue(TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(33));
        var start = DateTimeOffset.UnixEpoch;
        var first = new ControllerEffectState { LedColor = new EffectColor(1, 1, 1), LowFrequencyRumble = 0.5 };

        queue.Publish("pad", first);
        Assert.Single(queue.TakeDueWrites(start));

        queue.Publish("pad", first with { LedColor = new EffectColor(2, 2, 2) });
        Assert.Empty(queue.TakeDueWrites(start.AddMilliseconds(20)));
        Assert.Single(queue.TakeDueWrites(start.AddMilliseconds(34)));
    }

    [Fact]
    public void AMotorChangeStillUsesTheShortInterval()
    {
        var queue = new EffectDispatchQueue(TimeSpan.FromMilliseconds(16), TimeSpan.FromMilliseconds(33));
        var start = DateTimeOffset.UnixEpoch;
        var first = new ControllerEffectState { LedColor = new EffectColor(1, 1, 1), LowFrequencyRumble = 0.5 };

        queue.Publish("pad", first);
        _ = queue.TakeDueWrites(start);

        queue.Publish("pad", first with { LowFrequencyRumble = 0.9, LedColor = new EffectColor(2, 2, 2) });
        Assert.Single(queue.TakeDueWrites(start.AddMilliseconds(20)));
    }

    [Fact]
    public void DiffersOnlyInLightingIgnoresIdenticalStates()
    {
        var state = new ControllerEffectState { LedColor = new EffectColor(1, 1, 1) };

        Assert.False(state.DiffersOnlyInLighting(state));
        Assert.True(state.DiffersOnlyInLighting(state with { MicLed = MicLedState.On }));
        Assert.False(state.DiffersOnlyInLighting(state with { HighFrequencyRumble = 1 }));
    }

    [Fact]
    public void IndicatorsAloneSendTheDualSenseReportWithoutTriggerSections()
    {
        var plan = SdlGamepadEffectPlan.Create(new ControllerEffectState
        {
            LedColor = new EffectColor(9, 9, 9),
            PlayerLeds = 0x04,
            LeftTrigger = AdaptiveTriggerCommand.Release,
            RightTrigger = AdaptiveTriggerCommand.Release,
        });

        Assert.False(plan.ShouldSendTriggerReport(supportsAdaptiveTriggers: true, previouslyActivated: false));
        Assert.True(plan.ShouldSendDualSenseReport(supportsAdaptiveTriggers: true, triggersPreviouslyActivated: false));
        Assert.False(plan.ShouldSendDualSenseReport(supportsAdaptiveTriggers: false, triggersPreviouslyActivated: false));

        var report = new byte[DualSenseEffectEncoder.EffectStateLength];
        Assert.True(plan.TryWriteDualSenseReport(report, DualSenseRumbleMode.Enhanced, includeTriggers: false));
        Assert.Equal(0, report[0] & 0x0C); // no trigger enable bits
        Assert.Equal(0x10, report[1] & 0x10);
    }

    [Fact]
    public void GameLightingMergesPartialReportsAndAgesTheColour()
    {
        var clock = new ManualClock();
        var store = new GameLightingStore(clock);

        store.Update("slot", new GameLighting(Color: new LightColor(255, 0, 0)));
        clock.Advance(TimeSpan.FromSeconds(3));
        store.Update("slot", new GameLighting(PlayerIndicator: 0x1B));

        var state = store.Get("slot")!.Value;
        Assert.Equal(new LightColor(255, 0, 0), state.Color);
        Assert.Equal(3, state.ColorAgeSeconds, 3);
        Assert.Equal((byte)0x1B, state.PlayerLeds);

        store.Clear("slot");
        Assert.Null(store.Get("slot"));
    }

    [Fact]
    public void GameFlashIsConvertedFromTenMillisecondUnits()
    {
        var store = new GameLightingStore();
        store.Update("slot", new GameLighting(Color: new LightColor(1, 1, 1), FlashOn: 50, FlashOff: 25));

        var state = store.Get("slot")!.Value;
        Assert.Equal(0.5, state.FlashOnSeconds, 3);
        Assert.Equal(0.25, state.FlashOffSeconds, 3);
    }

    [Fact]
    public void ExternalRequestsPreferTheMostSpecificTargetAndExpire()
    {
        var clock = new ManualClock();
        var store = new ExternalLightingStore(clock);
        var all = new ExternalLightRequest(LightbarMode.Solid, new LightColor(1, 0, 0));
        var slot = new ExternalLightRequest(LightbarMode.Solid, new LightColor(0, 1, 0));
        var device = new ExternalLightRequest(LightbarMode.Solid, new LightColor(0, 0, 1));

        store.Set(ExternalLightingTarget.All, all, TimeSpan.FromSeconds(60));
        store.Set(new ExternalLightingTarget(ExternalLightingScope.Slot, "s1"), slot, TimeSpan.FromSeconds(10));
        store.Set(new ExternalLightingTarget(ExternalLightingScope.Device, "PAD"), device, TimeSpan.FromSeconds(5));

        Assert.Equal(device, store.Resolve("s1", "pad"));
        Assert.Equal(slot, store.Resolve("s1", "other"));
        Assert.Equal(all, store.Resolve("s2", "other"));

        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(slot, store.Resolve("s1", "pad"));

        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.Null(store.Resolve("s1", "pad"));
    }

    [Fact]
    public void ClearingAllReleasesEveryRequest()
    {
        var store = new ExternalLightingStore();
        store.Set(new ExternalLightingTarget(ExternalLightingScope.Device, "pad"),
            new ExternalLightRequest(LightbarMode.Solid, LightColor.Black), TimeSpan.FromSeconds(10));

        Assert.True(store.Clear(ExternalLightingTarget.All));
        Assert.Null(store.Resolve("slot", "pad"));
        Assert.Equal(0, store.ActiveCount);
    }

    [Fact]
    public void TimeToLiveIsCappedAtAnHour()
    {
        var clock = new ManualClock();
        var store = new ExternalLightingStore(clock);
        store.Set(ExternalLightingTarget.All, new ExternalLightRequest(LightbarMode.Solid, LightColor.Black), TimeSpan.FromDays(1));

        clock.Advance(TimeSpan.FromMinutes(61));
        Assert.Null(store.Resolve("slot", "pad"));
    }

    [Fact]
    public void TheFeedbackStoreSharesItsLightingStore()
    {
        var lighting = new GameLightingStore();
        Assert.Same(lighting, new RumbleFeedbackStore(lighting).Lighting);
        Assert.NotNull(new RumbleFeedbackStore().Lighting);
    }

    [Fact]
    public void EffectiveFeaturesMergeDetectionWithTheCatalog()
    {
        var dualSense = new InputDeviceInfo("pad", "DualSense", VendorId: 0x054C, ProductId: 0x0CE6)
        {
            Features = ControllerFeatures.Rumble | ControllerFeatures.PlayerLeds,
        };

        Assert.False(dualSense.EffectiveFeatures.HasFlag(ControllerFeatures.RgbLight));
        Assert.True(dualSense.EffectiveFeatures.HasFlag(ControllerFeatures.MicLed));
        Assert.Equal(ControllerFeatures.None, new InputDeviceInfo("kbd", "Keyboard").EffectiveFeatures);
    }

    [Fact]
    public void SlotDefaultsGetTheUnionOfTheAssignedPads()
    {
        using var fixture = new ResolverFixture();
        fixture.Catalog.ReplaceDevices("sdl",
        [
            new InputDeviceInfo("ds", "DualSense", VendorId: 0x054C, ProductId: 0x0CE6),
            new InputDeviceInfo("xb", "Xbox", VendorId: 0x045E, ProductId: 0x0B12),
        ]);
        fixture.Slots.AssignDevice(fixture.SlotId, "ds");
        fixture.Slots.AssignDevice(fixture.SlotId, "xb");

        var summary = fixture.Resolver.Resolve(fixture.SlotId, DeviceSettingsStore.SlotDefaultsDeviceId);

        Assert.True(summary.Features.HasFlag(ControllerFeatures.AdaptiveTriggers));
        Assert.True(summary.Features.HasFlag(ControllerFeatures.TriggerRumble));
        Assert.Equal(new[] { "DualSense", "Xbox Series" }, summary.FamilyNames.ToArray());
    }

    [Fact]
    public void AnOfflineAssignedPadMakesTheAnswerUnknown()
    {
        using var fixture = new ResolverFixture();
        fixture.Slots.AssignDevice(fixture.SlotId, "gone");

        Assert.Equal(
            ControllerFeatures.None,
            fixture.Resolver.Resolve(fixture.SlotId, DeviceSettingsStore.SlotDefaultsDeviceId).Features);
        Assert.Equal(ControllerFeatures.None, fixture.Resolver.Resolve(fixture.SlotId, "gone").Features);
    }

    [Fact]
    public void ASingleDeviceTargetDescribesThatDevice()
    {
        using var fixture = new ResolverFixture();
        fixture.Catalog.ReplaceDevices("sdl", [new InputDeviceInfo("ds4", "DS4", VendorId: 0x054C, ProductId: 0x09CC)]);

        var summary = fixture.Resolver.Resolve(fixture.SlotId, "ds4");

        Assert.True(summary.Features.HasFlag(ControllerFeatures.LightbarFlash));
        Assert.Equal(new[] { "DualShock 4" }, summary.FamilyNames.ToArray());
    }

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class ResolverFixture : IDisposable
    {
        private readonly string slotsFile = Path.Combine(
            Path.GetTempPath(), "gameflow-features-" + Guid.NewGuid().ToString("N") + ".json");

        public ResolverFixture()
        {
            Catalog = new InputDeviceCatalog(
                new Mock<ILocalizationService>().Object,
                new DeviceCategoryOverrideStore(NullLogger<DeviceCategoryOverrideStore>.Instance));
            Slots = new SlotRegistry(NullLogger<SlotRegistry>.Instance, slotsFile);
            SlotId = Slots.CreateSlot(VirtualControllerKind.DualSense)!.Id;
            Resolver = new ControllerFeatureResolver(Catalog, Slots);
        }

        public InputDeviceCatalog Catalog { get; }

        public SlotRegistry Slots { get; }

        public string SlotId { get; }

        public ControllerFeatureResolver Resolver { get; }

        public void Dispose() => File.Delete(slotsFile);
    }
}
