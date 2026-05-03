using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime;

internal sealed class WindowsHighResolutionTimerLease : IDisposable
{
    private readonly ILogger logger;
    private bool active;

    public WindowsHighResolutionTimerLease(ILogger logger)
    {
        this.logger = logger;

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            active = TimeBeginPeriod(1) == 0;
            if (!active)
            {
                logger.LogDebug("timeBeginPeriod(1) was not accepted. Falling back to normal timer resolution.");
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to enable 1 ms timer resolution.");
        }
    }

    public void Dispose()
    {
        if (!active)
        {
            return;
        }

        active = false;

        try
        {
            _ = TimeEndPeriod(1);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to restore timer resolution.");
        }
    }

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);
}
