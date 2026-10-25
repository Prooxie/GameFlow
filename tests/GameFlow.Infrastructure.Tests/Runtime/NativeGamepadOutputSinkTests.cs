using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.Native;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

/// <summary>Checks native output lifetime and failure handling.</summary>
public sealed class NativeGamepadOutputSinkTests
{
    /// <summary>Idle input does not submit duplicate native reports.</summary>
    [Fact]
    public async Task WriteIdenticalControlsSubmitsOneReportAsync()
    {
        var device = new Mock<INativeGamepadDevice>();
        var factory = new Mock<INativeGamepadDeviceFactory>();
        factory.Setup(value => value.Create()).Returns(device.Object);
        await using var sink = new NativeGamepadOutputSink(factory.Object, NullLogger<NativeGamepadOutputSink>.Instance);
        var snapshot = ControllerSnapshot.Empty();

        await sink.WriteAsync(snapshot, TestContext.Current.CancellationToken);
        await sink.WriteAsync(snapshot with { Timestamp = DateTimeOffset.UtcNow.AddSeconds(1) }, TestContext.Current.CancellationToken);

        device.Verify(value => value.Write(It.IsAny<ControllerSnapshot>()), Times.Once);
    }

    /// <summary>Missing platform permission remains visible while the runtime continues to produce preview snapshots.</summary>
    [Fact]
    public async Task WriteWithoutPermissionReportsUnavailableAndLimitsRetriesAsync()
    {
        var factory = new Mock<INativeGamepadDeviceFactory>();
        factory.Setup(value => value.Create()).Throws(new UnauthorizedAccessException("Missing permission"));
        await using var sink = new NativeGamepadOutputSink(factory.Object, NullLogger<NativeGamepadOutputSink>.Instance);

        await sink.WriteAsync(ControllerSnapshot.Empty(), TestContext.Current.CancellationToken);
        await sink.WriteAsync(ControllerSnapshot.Empty(), TestContext.Current.CancellationToken);

        Assert.StartsWith("native-unavailable:", sink.DisplayName);
        Assert.Null(sink.OwnedHardwareSignature);
        factory.Verify(value => value.Create(), Times.Once);
    }
    [Fact]
    public async Task WriteAndDisposeReleaseDeviceAsync()
    {
        var device = new Mock<INativeGamepadDevice>();
        var factory = new Mock<INativeGamepadDeviceFactory>();
        factory.Setup(value => value.Create()).Returns(device.Object);
        var sink = new NativeGamepadOutputSink(factory.Object, NullLogger<NativeGamepadOutputSink>.Instance);
        var snapshot = ControllerSnapshot.Empty("Pad") with { LeftTrigger = 1 };

        await sink.WriteAsync(snapshot, TestContext.Current.CancellationToken);
        Assert.Null(sink.OwnedHardwareSignature);
        await sink.DisposeAsync();
        await sink.DisposeAsync();

        device.Verify(value => value.Write(snapshot), Times.Once);
        device.Verify(value => value.Write(It.Is<ControllerSnapshot>(state => state.LeftTrigger == 0)), Times.Once);
        device.Verify(value => value.Dispose(), Times.Once);
        factory.Verify(value => value.Create(), Times.Once);
    }

    [Fact]
    public async Task WriteFailureDestroysDeviceAndReportsFailureAsync()
    {
        var device = new Mock<INativeGamepadDevice>();
        device.Setup(value => value.Write(It.IsAny<ControllerSnapshot>())).Throws(new IOException("Device disconnected"));
        var factory = new Mock<INativeGamepadDeviceFactory>();
        factory.Setup(value => value.Create()).Returns(device.Object);
        await using var sink = new NativeGamepadOutputSink(factory.Object, NullLogger<NativeGamepadOutputSink>.Instance);

        await sink.WriteAsync(ControllerSnapshot.Empty(), TestContext.Current.CancellationToken);

        Assert.StartsWith("native-unavailable:", sink.DisplayName);

        Assert.Null(sink.OwnedHardwareSignature);
        device.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task CancelledWriteDoesNotCreateDeviceAsync()
    {
        var factory = new Mock<INativeGamepadDeviceFactory>();
        await using var sink = new NativeGamepadOutputSink(factory.Object, NullLogger<NativeGamepadOutputSink>.Instance);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sink.WriteAsync(ControllerSnapshot.Empty(), new CancellationToken(true)).AsTask());

        factory.Verify(value => value.Create(), Times.Never);
    }
}
