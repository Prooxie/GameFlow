using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using GameFlow.Infrastructure.Runtime.Templates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.HidMaestro;

/// <summary>
/// The sink drives the backend seam. These tests pin which pad it asks for,
/// which signature it reserves while the pad is made, and when it gives up.
/// The fake backend stands in for the SDK.
/// </summary>
public sealed class HidMaestroOutputSinkTests
{
    private static readonly ControllerSnapshot Frame = ControllerSnapshot.Empty("test");

    /// <summary>A ready backend whose catalog holds the three curated profiles.</summary>
    private static FakeHidMaestroBackend CreateBackend()
    {
        var backend = new FakeHidMaestroBackend();
        backend.Catalog.AddRange(HidMaestroProfileCatalogService.FallbackProfiles);
        return backend;
    }

    private static HidMaestroOutputSink CreateSink(
        FakeHidMaestroBackend backend,
        DeviceOutputTemplate template,
        ILogger<HidMaestroOutputSink>? logger = null,
        TimeProvider? timeProvider = null)
    {
        var sink = new HidMaestroOutputSink(
            logger ?? NullLogger<HidMaestroOutputSink>.Instance, backend, timeProvider ?? TimeProvider.System);
        sink.Configure(template);
        return sink;
    }

    private static DeviceOutputTemplate Template(VirtualControllerKind kind, string profileId = "") =>
        new() { OutputKind = kind, OutputProfileId = profileId };

    private static ValueTask WriteAsync(HidMaestroOutputSink sink) =>
        sink.WriteAsync(Frame, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Write_UnchangedTemplate_CreatesOnePad()
    {
        var backend = CreateBackend();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360));

        await WriteAsync(sink);
        sink.Configure(Template(VirtualControllerKind.Xbox360));
        await WriteAsync(sink);

        Assert.Single(backend.Requests);
    }

    [Fact]
    public async Task Write_DualShock4Kind_RequestsTheDefaultProfile()
    {
        var backend = CreateBackend();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.DualShock4));

        await WriteAsync(sink);

        Assert.Equal(new HidMaestroPadRequest("dualshock-4-v1-full"), backend.Requests[0]);
    }

    [Fact]
    public async Task Write_ExplicitProfileNotInCatalog_FallsBackToTheKindDefault()
    {
        var backend = CreateBackend();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360, "nope"));

        await WriteAsync(sink);

        Assert.Equal(new HidMaestroPadRequest("xbox-360-wired"), backend.Requests[0]);
    }

    [Fact]
    public async Task Write_ExplicitProfileNotDeployable_FallsBackToTheKindDefault()
    {
        var backend = CreateBackend();
        backend.Catalog.Add(new HidMaestroCatalogProfile(
            "wheel-only", "Catalog-only wheel", "Test", 0x1234, 0x5678, 10, 4, true, "usb", IsDeployable: false));
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360, "wheel-only"));

        await WriteAsync(sink);

        Assert.Equal(new HidMaestroPadRequest("xbox-360-wired"), Assert.Single(backend.Requests));
    }

    [Fact]
    public async Task Write_ExplicitProfileInAnotherCase_RequestsTheCatalogSpelling()
    {
        var backend = CreateBackend();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360, " DualSense "));

        await WriteAsync(sink);

        Assert.Equal(new HidMaestroPadRequest("dualsense"), backend.Requests[0]);
    }

    [Fact]
    public async Task Write_GenericKind_RequestsTheBuiltShape()
    {
        var backend = CreateBackend();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.GenericDirectInput));

        await WriteAsync(sink);

        var request = Assert.Single(backend.Requests);
        Assert.Equal("gameflow-custom-beeff001", request.ProfileId);
        Assert.Equal(
            new HidMaestroGenericShape("GameFlow Game Controller", 0xBEEF, 0xF001, 2, 2, 11, 1),
            request.Generic);
    }

    [Fact]
    public async Task OwnedSignature_DuringCreate_IsTheCatalogEntry()
    {
        var backend = CreateBackend();
        HidMaestroOutputSink? sink = null;
        (ushort Vid, ushort Pid)? signature = null;
        DateTimeOffset? activatedAt = null;
        backend.CreatePad = request =>
        {
            signature = sink!.OwnedHardwareSignature;
            activatedAt = sink.OwnedSignatureActivatedAt;
            return new FakeHidMaestroPad { ProfileId = request.ProfileId };
        };
        await using var created = sink = CreateSink(backend, Template(VirtualControllerKind.DualShock4));

        await WriteAsync(sink);

        // The catalog DS4 is 054C:05C4. The old per-kind guess was 054C:09CC.
        Assert.Equal(((ushort)0x054C, (ushort)0x05C4), signature);
        Assert.NotNull(activatedAt);
    }

    [Fact]
    public async Task OwnedSignature_DuringGenericCreate_IsTheTemplateIdentity()
    {
        var backend = CreateBackend();
        HidMaestroOutputSink? sink = null;
        (ushort Vid, ushort Pid)? signature = null;
        backend.CreatePad = request =>
        {
            signature = sink!.OwnedHardwareSignature;
            return new FakeHidMaestroPad { ProfileId = request.ProfileId };
        };
        await using var created = sink = CreateSink(backend, Template(VirtualControllerKind.GenericDirectInput));

        await WriteAsync(sink);

        Assert.Equal(((ushort)0xBEEF, (ushort)0xF001), signature);
    }

    [Fact]
    public async Task Write_ProfileNotInCatalog_CreatesWithoutReservingASignature()
    {
        var backend = new FakeHidMaestroBackend();
        HidMaestroOutputSink? sink = null;
        (ushort Vid, ushort Pid)? signature = (0, 0);
        DateTimeOffset? activatedAt = DateTimeOffset.MinValue;
        backend.CreatePad = request =>
        {
            signature = sink!.OwnedHardwareSignature;
            activatedAt = sink.OwnedSignatureActivatedAt;
            return new FakeHidMaestroPad { ProfileId = request.ProfileId };
        };
        await using var created = sink = CreateSink(backend, Template(VirtualControllerKind.DualSense));

        await WriteAsync(sink);

        Assert.Equal(new HidMaestroPadRequest("dualsense"), Assert.Single(backend.Requests));
        Assert.Null(signature);
        Assert.Null(activatedAt);
    }

    [Fact]
    public async Task OwnedSignature_ActivePad_IsThePadIdentity()
    {
        var backend = CreateBackend();
        backend.CreatePad = request => new FakeHidMaestroPad { ProfileId = request.ProfileId, HardwareSignature = (0x1234, 0x5678) };
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.DualShock4));

        await WriteAsync(sink);

        Assert.Equal(((ushort)0x1234, (ushort)0x5678), sink.OwnedHardwareSignature);
        Assert.NotNull(sink.OwnedSignatureActivatedAt);
    }

    [Fact]
    public async Task Write_CreateFails_NoRetryInsideTheCooldown()
    {
        var backend = CreateBackend();
        backend.CreatePad = _ => null;
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360));

        await WriteAsync(sink);
        await WriteAsync(sink);

        Assert.Single(backend.Requests);
        Assert.StartsWith("HIDMaestro unavailable — no output (", sink.DisplayName, StringComparison.Ordinal);
        Assert.Contains("fake failure", sink.DisplayName, StringComparison.Ordinal);
        Assert.Null(sink.OwnedSignatureActivatedAt);
    }

    [Fact]
    public async Task Write_CreateThrows_ClearsReservationAndWaitsForTheCooldown()
    {
        var backend = CreateBackend();
        backend.ThrowOnCreate = new InvalidOperationException("fake create crash");
        var logger = new HidMaestroUnavailableLoggingTests.CountingLogger();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360), logger);

        await WriteAsync(sink);
        await WriteAsync(sink);

        Assert.Single(backend.Requests);
        Assert.Null(sink.OwnedSignatureActivatedAt);
        Assert.Null(sink.OwnedHardwareSignature);
        Assert.StartsWith("HIDMaestro unavailable — no output (", sink.DisplayName, StringComparison.Ordinal);
        Assert.Contains("fake create crash", sink.DisplayName, StringComparison.Ordinal);
        Assert.Equal(1, logger.Problems);
    }

    [Fact]
    public async Task Write_SdkMissing_NeverRequestsAPad()
    {
        var backend = CreateBackend();
        backend.Status = new HidMaestroStatus(
            HidMaestroState.SdkMissing, "HIDMaestro.Core.dll not found. Place the SDK assembly next to the executable.");
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360));

        await WriteAsync(sink);

        Assert.Empty(backend.Requests);
        Assert.Contains("HIDMaestro.Core.dll not found", sink.DisplayName, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, -3600)]
    [InlineData(false, 86400)]
    [InlineData(true, -3600)]
    [InlineData(true, 86400)]
    public async Task Write_Unavailable_ClockAdjustmentDoesNotChangeTheRetryCooldown(
        bool creationFails, int wallClockJumpSeconds)
    {
        var backend = CreateBackend();
        var ready = backend.Status;
        backend.Status = creationFails ? ready : new HidMaestroStatus(HidMaestroState.SdkMissing, "missing SDK");
        backend.CreatePad = _ => null;
        var time = new AdjustableTime();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360), timeProvider: time);
        await WriteAsync(sink);
        var initialRequests = backend.Requests.Count;

        backend.Status = ready;
        backend.CreatePad = request => new FakeHidMaestroPad { ProfileId = request.ProfileId };
        time.WallTime += TimeSpan.FromSeconds(wallClockJumpSeconds);
        time.Advance(TimeSpan.FromMilliseconds(44999));
        await WriteAsync(sink);
        Assert.Contains("unavailable", sink.DisplayName, StringComparison.Ordinal);
        Assert.Equal(initialRequests, backend.Requests.Count);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await WriteAsync(sink);
        Assert.Equal("HIDMaestro — Fake pad", sink.DisplayName);
        Assert.Equal(initialRequests + 1, backend.Requests.Count);
    }

    [Theory]
    [InlineData(-3600)]
    [InlineData(86400)]
    public async Task Write_BrokenPad_ClockAdjustmentDoesNotChangeTheReplacementCooldown(int wallClockJumpSeconds)
    {
        var backend = CreateBackend();
        var broken = new FakeHidMaestroPad { ThrowOnSubmit = true };
        backend.CreatePad = _ => backend.Requests.Count == 1 ? broken : new FakeHidMaestroPad();
        var time = new AdjustableTime();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360), timeProvider: time);
        for (var frame = 0; frame < HidMaestroOutputSink.MaxConsecutiveSubmitFailures; frame++)
        {
            await WriteAsync(sink);
        }
        Assert.True(broken.Disposed);

        time.WallTime += TimeSpan.FromSeconds(wallClockJumpSeconds);
        time.Advance(TimeSpan.FromMilliseconds(299999));
        await WriteAsync(sink);
        Assert.Single(backend.Requests);

        time.Advance(TimeSpan.FromMilliseconds(1));
        await WriteAsync(sink);
        Assert.Equal(2, backend.Requests.Count);
        Assert.Equal("HIDMaestro — Fake pad", sink.DisplayName);
    }

    [Fact]
    public async Task Write_ConsecutiveSubmitFailures_DetachAtTheLimit()
    {
        var backend = CreateBackend();
        var pad = new FakeHidMaestroPad { ThrowOnSubmit = true };
        backend.CreatePad = _ => pad;
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360));

        for (var frame = 1; frame < HidMaestroOutputSink.MaxConsecutiveSubmitFailures; frame++)
        {
            await WriteAsync(sink);
        }

        Assert.False(pad.Disposed);

        await WriteAsync(sink);

        Assert.True(pad.Disposed);
        Assert.Contains("submit failed repeatedly", sink.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Write_SubmitSucceedsBetweenFailures_ResetsTheCount()
    {
        var backend = CreateBackend();
        var pad = new FakeHidMaestroPad { ThrowOnSubmit = true };
        backend.CreatePad = _ => pad;
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360));

        for (var frame = 1; frame < HidMaestroOutputSink.MaxConsecutiveSubmitFailures; frame++)
        {
            await WriteAsync(sink);
        }

        pad.ThrowOnSubmit = false;
        await WriteAsync(sink);
        pad.ThrowOnSubmit = true;

        for (var frame = 1; frame < HidMaestroOutputSink.MaxConsecutiveSubmitFailures; frame++)
        {
            await WriteAsync(sink);
        }

        Assert.False(pad.Disposed);
        Assert.Equal("HIDMaestro — Fake pad", sink.DisplayName);
    }

    [Fact]
    public async Task Configure_DifferentKind_StopsRumbleBeforeDisposingTheOldPad()
    {
        var backend = CreateBackend();
        var pad = new FakeHidMaestroPad();
        backend.CreatePad = _ => pad;
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360));
        await WriteAsync(sink);

        bool? disposedAtStop = null;
        sink.RumbleReceived += (low, high) =>
        {
            if (low == 0d && high == 0d)
            {
                disposedAtStop = pad.Disposed;
            }
        };

        sink.Configure(Template(VirtualControllerKind.DualSense));

        Assert.False(disposedAtStop);
        Assert.True(pad.Disposed);
    }

    [Fact]
    public async Task Configure_PadDisposeThrows_StillReconfigures()
    {
        var backend = CreateBackend();
        var first = new FakeHidMaestroPad { ThrowOnDispose = true };
        var pads = new Queue<FakeHidMaestroPad>([first, new FakeHidMaestroPad { ProfileName = "Second pad" }]);
        backend.CreatePad = _ => pads.Dequeue();
        var logger = new HidMaestroUnavailableLoggingTests.CountingLogger();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360), logger);
        await WriteAsync(sink);

        bool? disposedAtStop = null;
        sink.RumbleReceived += (low, high) =>
        {
            if (low == 0d && high == 0d)
            {
                disposedAtStop = first.Disposed;
            }
        };

        sink.Configure(Template(VirtualControllerKind.DualSense));
        await WriteAsync(sink);

        Assert.False(disposedAtStop);
        Assert.True(first.Disposed);
        Assert.Equal(1, logger.Problems);
        Assert.Equal(2, backend.Requests.Count);
        Assert.Equal("HIDMaestro — Second pad", sink.DisplayName);
    }

    [Fact]
    public async Task DisposeAsync_PadDisposeThrows_DoesNotThrow()
    {
        var backend = CreateBackend();
        var pad = new FakeHidMaestroPad { ThrowOnDispose = true };
        backend.CreatePad = _ => pad;
        var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360));
        await WriteAsync(sink);

        await sink.DisposeAsync();

        Assert.True(pad.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Teardown_RumbleSubscriberThrows_StillStopsOtherSubscribersAndReleasesThePad(bool dispose)
    {
        var backend = CreateBackend();
        var pad = new FakeHidMaestroPad();
        backend.CreatePad = _ => pad;
        var logger = new HidMaestroUnavailableLoggingTests.CountingLogger();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360), logger);
        await WriteAsync(sink);
        sink.RumbleReceived += (_, _) => throw new InvalidOperationException("subscriber failed");
        (double Low, double High)? received = null;
        sink.RumbleReceived += (low, high) => received = (low, high);

        if (dispose)
        {
            await sink.DisposeAsync();
        }
        else
        {
            sink.Configure(Template(VirtualControllerKind.DualSense));
        }

        Assert.True(pad.Disposed);
        Assert.Equal((0d, 0d), received);
        Assert.Equal(1, logger.Problems);
    }

    [Fact]
    public async Task PadRumble_IsForwarded()
    {
        var backend = CreateBackend();
        var pad = new FakeHidMaestroPad();
        backend.CreatePad = _ => pad;
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360));
        await WriteAsync(sink);

        (double Low, double High)? received = null;
        sink.RumbleReceived += (low, high) => received = (low, high);

        pad.RaiseRumble(0.5, 0.25);

        Assert.Equal((0.5, 0.25), received);
    }

    [Fact]
    public async Task FinalizeAfterRebuild_OnlyWithAnActivePad()
    {
        var backend = CreateBackend();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.Xbox360));

        sink.FinalizeAfterRebuild();
        Assert.Equal(0, backend.FinalizeNamesCalls);

        await WriteAsync(sink);
        sink.FinalizeAfterRebuild();
        Assert.Equal(1, backend.FinalizeNamesCalls);
    }

    [Fact]
    public async Task Write_UsbSonyPad_WarnsOnce()
    {
        var backend = CreateBackend();
        backend.CreatePad = _ => new FakeHidMaestroPad { ProfileId = "dualsense", CarriesExtendedInput = false };
        var logger = new HidMaestroUnavailableLoggingTests.CountingLogger();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.DualSense), logger);

        for (var frame = 0; frame < 10; frame++)
        {
            await WriteAsync(sink);
        }

        Assert.Equal(1, logger.Problems);
    }

    [Fact]
    public async Task Write_SonyPadThatCarriesExtendedInput_DoesNotWarn()
    {
        var backend = CreateBackend();
        backend.CreatePad = _ => new FakeHidMaestroPad { ProfileId = "dualsense-bt", CarriesExtendedInput = true };
        var logger = new HidMaestroUnavailableLoggingTests.CountingLogger();
        await using var sink = CreateSink(backend, Template(VirtualControllerKind.DualSense, "dualsense"), logger);

        await WriteAsync(sink);

        Assert.Equal(0, logger.Problems);
    }

    private sealed class AdjustableTime : TimeProvider
    {
        public DateTimeOffset WallTime { get; set; } = DateTimeOffset.UnixEpoch;
        private long timestamp;

        public override DateTimeOffset GetUtcNow() => WallTime;
        public override long GetTimestamp() => timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public void Advance(TimeSpan elapsed)
        {
            timestamp += elapsed.Ticks;
            WallTime += elapsed;
        }
    }
}
