using GameFlow.Core.Models;
using GameFlow.Core.Pipeline;
using GameFlow.Infrastructure.Localization;
using GameFlow.Infrastructure.Runtime;
using GameFlow.Infrastructure.Runtime.Effects;
using GameFlow.Infrastructure.Runtime.Slots;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Effects;

public sealed class ControllerEffectProducerTests
{
    /// <summary>Checks that an idle effects pass creates no temporary collections.</summary>
    [Fact]
    public void ProduceOnceEmptyRegistryAllocatesNothingAfterWarmup()
    {
        var path = Path.Combine(Path.GetTempPath(), "gameflow-effects-" + Guid.NewGuid().ToString("N") + ".json");
        var slots = new SlotRegistry(NullLogger<SlotRegistry>.Instance, path);
        var settings = new DeviceSettingsStore(NullLogger<DeviceSettingsStore>.Instance, path);
        var catalog = new InputDeviceCatalog(Mock.Of<ILocalizationService>(),
            new DeviceCategoryOverrideStore(NullLogger<DeviceCategoryOverrideStore>.Instance));
        using var effects = new ControllerEffectsService(Mock.Of<IControllerEffectWriter>(),
            NullLogger<ControllerEffectsService>.Instance);
        using var producer = new ControllerEffectProducer(slots, new SlotSnapshotStore(), settings, catalog,
            new RumbleFeedbackStore(), effects, NullLogger<ControllerEffectProducer>.Instance);
        for (var index = 0; index < 100; index++)
        {
            producer.ProduceOnce();
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
        {
            producer.ProduceOnce();
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(0, allocated);
    }

    private static ResolvedAdaptiveTrigger Resolved(AdaptiveTriggerSettings settings) =>
        ResolvedAdaptiveTrigger.From(settings);

    [Fact]
    public void OffSettingsBecomeAnExplicitReleaseCommand()
    {
        var command = ControllerEffectProducer.ToCommand(Resolved(new AdaptiveTriggerSettings
        {
            Mode = AdaptiveTriggerMode.Off,
        }));

        Assert.Equal(AdaptiveTriggerEffect.Off, command.Effect);
    }

    [Fact]
    public void VibrationFrequencySurvivesTheTransportConversion()
    {
        var command = ControllerEffectProducer.ToCommand(Resolved(new AdaptiveTriggerSettings
        {
            Mode = AdaptiveTriggerMode.Vibration,
            StartPosition = 0.2f,
            EndPosition = 0.8f,
            Strength = 0.7f,
            FrequencyHz = 43,
        }));

        Assert.Equal(AdaptiveTriggerEffect.Vibration, command.Effect);
        Assert.Equal(43, command.FrequencyHz);
    }

    /// <summary>
    /// The rumble link changes the STRENGTH byte and nothing else about
    /// the transport conversion — the whole point of resolving in Core is
    /// that this layer cannot tell a linked trigger from a static one.
    /// </summary>
    [Fact]
    public void ALinkedTriggerConvertsThroughTheSamePathAsAStaticOne()
    {
        var settings = new AdaptiveTriggerSettings
        {
            Mode = AdaptiveTriggerMode.Weapon,
            StartPosition = 0.25f,
            EndPosition = 0.75f,
            Strength = 1.0f,
            FeedbackLink = TriggerFeedbackLink.Resistance,
        };

        var linked = ControllerEffectProducer.ToCommand(
            EffectSceneResolver.ResolveAdaptiveTrigger(settings, new EffectSceneContext(0, RumbleLevel: 0.5)));
        var equivalentStatic = ControllerEffectProducer.ToCommand(
            Resolved(settings with { Strength = 0.5f, FeedbackLink = TriggerFeedbackLink.None }));

        Assert.Equal(equivalentStatic, linked);
    }
}
