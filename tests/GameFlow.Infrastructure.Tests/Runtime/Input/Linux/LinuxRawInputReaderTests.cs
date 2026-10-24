using System.Collections.Concurrent;
using GameFlow.Infrastructure.Runtime.Input.Linux;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Input.Linux;

public sealed class LinuxRawInputReaderTests
{
    [Fact]
    public void PreviewAndRuntimeReceiveTheSameMovementWithoutConsumingEachOther()
    {
        var transport = new FakeTransport { Devices = [new("/dev/input/event9", "Test mouse", false, true)] };
        using var reader = new LinuxRawInputReader(NullLogger<LinuxRawInputReader>.Instance, transport, watchForChanges: false);
        var id = EvdevDeviceDiscovery.BuildDeviceId("mouse", "/dev/input/event9");
        transport.Events.Enqueue(new EvdevInterop.InputEvent { Type = EvdevInterop.EV_REL, Code = EvdevInterop.REL_X, Value = 42 });
        transport.Events.Enqueue(new EvdevInterop.InputEvent { Type = EvdevInterop.EV_REL, Code = EvdevInterop.REL_WHEEL, Value = 1 });
        transport.Events.Enqueue(new EvdevInterop.InputEvent { Type = EvdevInterop.EV_KEY, Code = EvdevInterop.BTN_LEFT, Value = 1 });
        Assert.True(SpinWait.SpinUntil(() => reader.ReadMouseFrame(id).Left, TimeSpan.FromSeconds(3)));
        // The polling runtime above drained its own deltas. The preview must retain them.
        var preview = reader.ReadMousePreviewFrame(id);
        Assert.Equal(42, preview.Dx);
        Assert.Equal(120, preview.WheelDelta);
        Assert.True(preview.Left);
        Assert.Equal(0, reader.ReadMousePreviewFrame(id).Dx);
        Assert.Equal(0, reader.ReadMouseFrame(id).Dx);
        Assert.Equal(default, reader.ReadMousePreviewFrame("another mouse"));
    }

    [Fact]
    public void UnplugClearsHeldKeysAndReconnectStartsANewReader()
    {
        var transport = new FakeTransport();
        using var reader = new LinuxRawInputReader(NullLogger<LinuxRawInputReader>.Instance, transport, watchForChanges: false);
        transport.Events.Enqueue(new EvdevInterop.InputEvent { Type = EvdevInterop.EV_KEY, Code = 30, Value = 1 }); // A
        Assert.True(SpinWait.SpinUntil(() => reader.GetPressedKeysAggregate().Contains(0x41), TimeSpan.FromSeconds(3)));
        transport.Devices = [];
        reader.RefreshDevices();
        Assert.Empty(reader.GetPressedKeysAggregate());
        Assert.True(SpinWait.SpinUntil(() => transport.Closed.Count == 1, TimeSpan.FromSeconds(3)));

        transport.Devices = FakeTransport.Keyboard;
        reader.RefreshDevices();
        transport.Events.Enqueue(new EvdevInterop.InputEvent { Type = EvdevInterop.EV_KEY, Code = 30, Value = 1 });
        Assert.True(SpinWait.SpinUntil(() => reader.GetPressedKeysAggregate().Contains(0x41), TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void FailedReadNeutralizesStateAndDisposeClosesOnTheReaderThread()
    {
        var transport = new FakeTransport();
        var reader = new LinuxRawInputReader(NullLogger<LinuxRawInputReader>.Instance, transport, watchForChanges: false);
        transport.Events.Enqueue(new EvdevInterop.InputEvent { Type = EvdevInterop.EV_KEY, Code = 30, Value = 1 });
        Assert.True(SpinWait.SpinUntil(() => reader.GetPressedKeysAggregate().Count > 0, TimeSpan.FromSeconds(3)));
        transport.Fail = true;
        Assert.True(SpinWait.SpinUntil(() => reader.GetPressedKeysAggregate().Count == 0, TimeSpan.FromSeconds(3)));
        var caller = Environment.CurrentManagedThreadId;
        reader.Dispose();
        Assert.True(SpinWait.SpinUntil(() => transport.Closed.Count == 1, TimeSpan.FromSeconds(3)));
        Assert.DoesNotContain(caller, transport.Closed);
    }

    private sealed class FakeTransport : IEvdevTransport
    {
        internal static readonly EvdevDeviceInfo[] Keyboard = [new("/dev/input/event9", "Test keyboard", true, false)];
        internal IReadOnlyList<EvdevDeviceInfo> Devices = Keyboard;
        internal readonly ConcurrentQueue<EvdevInterop.InputEvent> Events = new();
        internal readonly ConcurrentBag<int> Closed = new();
        internal volatile bool Fail;
        private int nextFd;
        public IReadOnlyList<EvdevDeviceInfo> Scan() => Devices;
        public int Open(string path) => Interlocked.Increment(ref nextFd);
        public int WaitForInput(int fd)
        {
            if (Fail) { return -1; }
            if (!Events.IsEmpty) { return 1; }
            Thread.Sleep(5);
            return 0;
        }
        public int Read(int fd, out EvdevInterop.InputEvent input) => Events.TryDequeue(out input) ? 1 : 0;
        public void Close(int fd) => Closed.Add(Environment.CurrentManagedThreadId);
    }
}
