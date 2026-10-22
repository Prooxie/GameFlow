using GameFlow.Core.Enums;
using GameFlow.Core.Models;
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

public sealed class SlotFailureSafetyTests
{
    [Fact]
    public async Task ReadFailureReleasesPreviouslyHeldControlsAndReportsRecovery()
    {
        var directory = Directory.CreateTempSubdirectory("gameflow-slot-safety-").FullName;
        try
        {
            var registry = new SlotRegistry(NullLogger<SlotRegistry>.Instance, Path.Combine(directory, "slots.json"));
            var slot = registry.CreateSlot(VirtualControllerKind.Xbox360)!;
            registry.SetEnabled(slot.Id, true);
            registry.AssignDevice(slot.Id, "device");
            var sink = new RecordingSink();
            var factory = new Mock<IOutputSinkFactory>();
            factory.Setup(value => value.Create(It.IsAny<string>())).Returns(sink);
            var snapshots = new SlotSnapshotStore();
            await using var runtime = new SlotRuntime(registry, factory.Object, snapshots, new InMemoryProfileRepository(), new NullMouseOutputWriter(),
                new DeviceSettingsStore(NullLogger<DeviceSettingsStore>.Instance, Path.Combine(directory, "devices.json")), new RumbleFeedbackStore(), NullLogger.Instance);
            await runtime.RebuildAsync(new ProfileDocument(), "preview");
            var buttons = ButtonMask.Empty;
            buttons[ButtonId.South] = true;
            var input = new Mock<IMultiDeviceInputSource>();
            input.SetupSequence(value => value.ReadDevice("device"))
                .Returns(ControllerSnapshot.Empty().WithButtons(buttons))
                .Throws(new IOException("Device disconnected"))
                .Returns(ControllerSnapshot.Empty());

            await runtime.TickAsync(input.Object, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            Assert.True(sink.Last.IsPressed(ButtonId.South));
            await runtime.TickAsync(input.Object, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            Assert.False(sink.Last.IsPressed(ButtonId.South));
            Assert.False(snapshots.Get(slot.Id).Virtual.IsPressed(ButtonId.South));
            Assert.Contains("controls released", snapshots.GetOutputStatus(slot.Id));
            await runtime.TickAsync(input.Object, DateTimeOffset.UtcNow, TestContext.Current.CancellationToken);
            Assert.Equal(sink.DisplayName, snapshots.GetOutputStatus(slot.Id));
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
