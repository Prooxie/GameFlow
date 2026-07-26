using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>
/// Keeps the device catalog in step with which phones are currently
/// connected, so a web pad appears in the slot device picker as soon as
/// someone opens the page and disappears when they close it.
///
/// <para>
/// Polls rather than pushing on connect/disconnect: the hub already
/// expires pads that go silent (a phone leaving Wi-Fi range never sends
/// a close frame), so the catalog has to be re-derived on a timer
/// regardless — an event on connect wouldn't cover the disappearing case.
/// </para>
/// </summary>
public sealed class WebControllerEnumerationService(
    WebControllerHub hub,
    InputDeviceCatalog catalog,
    ILogger<WebControllerEnumerationService> logger) : BackgroundService
{
    private readonly WebControllerHub hub = hub;
    private readonly InputDeviceCatalog catalog = catalog;
    private readonly ILogger<WebControllerEnumerationService> logger = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                catalog.ReplaceDevices(WebControllerDeviceScanner.SourceKey, WebControllerDeviceScanner.Scan(hub));
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Web controller enumeration failed.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
