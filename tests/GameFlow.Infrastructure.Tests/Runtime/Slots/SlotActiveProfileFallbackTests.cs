using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using GameFlow.Infrastructure.Runtime;
using GameFlow.Infrastructure.Runtime.Effects;
using GameFlow.Infrastructure.Runtime.Input;
using GameFlow.Infrastructure.Runtime.Slots;
using GameFlow.Infrastructure.Runtime.Templates;
using GameFlow.Infrastructure.Tests.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Slots;

/// <summary>
/// Rules edited by clicking a virtual controller are saved into the active
/// profile. A slot with no profiles of its own must run that profile, or the
/// rules never reach the virtual pad.
/// </summary>
public sealed class SlotActiveProfileFallbackTests
{
    private static readonly ProfileDocument RemapProfile = new()
    {
        Id = "active",
        Name = "Active",
        Rules = [new ButtonRemapRule { Id = "remap", SourceButton = ButtonId.South, TargetButton = ButtonId.North }],
    };

    [Fact]
    public async Task SlotWithoutProfilesRunsTheActiveProfileRules()
    {
        var (sink, _) = await TickOnceAsync(assignProfile: null);

        Assert.True(sink.Last.IsPressed(ButtonId.North));
        Assert.False(sink.Last.IsPressed(ButtonId.South));
    }

    [Fact]
    public async Task SlotWithItsOwnProfileIgnoresTheActiveProfile()
    {
        var (sink, _) = await TickOnceAsync(assignProfile: new ProfileDocument { Id = "own", Name = "Own", Rules = [] });

        Assert.True(sink.Last.IsPressed(ButtonId.South));
        Assert.False(sink.Last.IsPressed(ButtonId.North));
    }

    private static async Task<(RecordingSink Sink, SlotSnapshotStore Snapshots)> TickOnceAsync(ProfileDocument? assignProfile)
    {
        var directory = Directory.CreateTempSubdirectory("gameflow-slot-fallback-").FullName;
        try
        {
            var registry = new SlotRegistry(NullLogger<SlotRegistry>.Instance, Path.Combine(directory, "slots.json"));
            var slot = registry.CreateSlot(VirtualControllerKind.Xbox360)!;
            registry.SetEnabled(slot.Id, true);
            registry.AssignDevice(slot.Id, "device");

            var profiles = new InMemoryProfileRepository();
            if (assignProfile is not null)
            {
                await profiles.SaveAsync(assignProfile);
                registry.SetProfiles(slot.Id, [assignProfile.Id]);
            }

            var sink = new RecordingSink();
            var factory = new Mock<IOutputSinkFactory>();
            factory.Setup(value => value.Create(It.IsAny<string>())).Returns(sink);
            var snapshots = new SlotSnapshotStore();
            await using var runtime = new SlotRuntime(registry, factory.Object, snapshots, profiles, new NullMouseOutputWriter(),
                new DeviceSettingsStore(NullLogger<DeviceSettingsStore>.Instance, Path.Combine(directory, "devices.json")),
                new RumbleFeedbackStore(), NullLogger.Instance);
            await runtime.RebuildAsync(RemapProfile, "preview");

            var buttons = ButtonMask.Empty;
            buttons[ButtonId.South] = true;
            var input = new Mock<IMultiDeviceInputSource>();
            input.Setup(value => value.ReadDevice("device")).Returns(ControllerSnapshot.Empty().WithButtons(buttons));

            await runtime.TickAsync(input.Object, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            return (sink, snapshots);
        }
        finally
        {
            foreach (var file in Directory.EnumerateFiles(directory)) { File.Delete(file); }
            Directory.Delete(directory);
        }
    }

    private sealed class RecordingSink : IOutputSink
    {
        public string DisplayName => "Test output";
        public ControllerSnapshot Last = ControllerSnapshot.Empty();
        public ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken) { Last = snapshot; return ValueTask.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
