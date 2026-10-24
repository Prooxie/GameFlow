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
    ILogger<WebControllerEnumerationService> logger,
    PhoneRegistry? registry = null) : BackgroundService
{
    private readonly WebControllerHub hub = hub;
    private readonly PhoneRegistry? registry = registry;
    private readonly InputDeviceCatalog catalog = catalog;
    private readonly ILogger<WebControllerEnumerationService> logger = logger;

    private readonly SemaphoreSlim registryChanged = new(0, 1);

    private void OnRegistryChanged(object? sender, EventArgs e)
    {
        try
        {
            registryChanged.Release();
        }
        catch (SemaphoreFullException)
        {
            // A refresh is already pending.
        }
    }

    public override void Dispose()
    {
        if (registry is not null)
        {
            registry.Changed -= OnRegistryChanged;
        }
        registryChanged.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (registry is not null)
        {
            registry.Changed += OnRegistryChanged;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                catalog.ReplaceDevices(WebControllerDeviceScanner.SourceKey, WebControllerDeviceScanner.Scan(hub, registry));
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Web controller enumeration failed.");
            }

            try
            {
                // A phone added or renamed in the app should list at once,
                // not on the next poll — the add flow selects it straight away.
                _ = await registryChanged.WaitAsync(TimeSpan.FromSeconds(1), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
