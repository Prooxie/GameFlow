using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Input.Linux;

internal interface IEvdevTransport
{
    IReadOnlyList<EvdevDeviceInfo> Scan();
    int Open(string path);
    int WaitForInput(int fd);
    int Read(int fd, out EvdevInterop.InputEvent input);
    void Close(int fd);
}

internal sealed class EvdevTransport(ILogger logger) : IEvdevTransport
{
    public IReadOnlyList<EvdevDeviceInfo> Scan() => EvdevDeviceDiscovery.Scan(logger);
    public int Open(string path) => EvdevInterop.OpenNonBlocking(path);
    public int WaitForInput(int fd)
    {
        var descriptor = new EvdevInterop.PollFd { Fd = fd, Events = 1 };
        var result = EvdevInterop.poll(ref descriptor, 1, 100);
        if (result < 0) { return Marshal.GetLastPInvokeError() == 4 ? 0 : -1; }
        if ((descriptor.ReturnedEvents & (8 | 16 | 32)) != 0) { return -1; }
        return result > 0 && (descriptor.ReturnedEvents & 1) != 0 ? 1 : 0;
    }
    public int Read(int fd, out EvdevInterop.InputEvent input)
    {
        input = default;
        var count = EvdevInterop.read(fd, ref input, (nuint)EvdevInterop.InputEventSize);
        if (count == EvdevInterop.InputEventSize) { return 1; }
        if (count == 0) { return -1; }
        if (count < 0 && Marshal.GetLastPInvokeError() is not (4 or 11)) { return -1; }
        return 0;
    }
    public void Close(int fd) => EvdevInterop.close(fd);
}
