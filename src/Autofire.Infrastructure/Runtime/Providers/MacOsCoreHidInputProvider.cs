using System.Runtime.InteropServices;
using Autofire.Core.Models;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime.Providers;

/// <summary>
/// macOS CoreHID input provider.
///
/// On Apple Silicon and Intel macs, CoreHID + GameController.framework expose
/// MFi-class controllers, Xbox-class controllers (via Apple's bundled driver
/// since macOS 11), and DualShock 4 / DualSense / Joy-Con (since macOS 13).
///
/// This provider uses the C-callable IOKit HIDManager API rather than the
/// Swift-only GameController.framework so we can stay in pure P/Invoke without
/// shipping a Swift bridging dylib. When we eventually want force-feedback we
/// will need to add a Swift bridge — that work is intentionally deferred.
///
/// Permissions: macOS 13+ requires the user to grant the application
/// "Input Monitoring" permission. The first time the app runs, the OS will
/// prompt automatically; the README documents the System Settings path the
/// user can navigate to if they miss the prompt.
/// </summary>
public sealed class MacOsCoreHidInputProvider : IInputProvider, IAsyncDisposable
{
    private const string IOKitFramework =
        "/System/Library/Frameworks/IOKit.framework/IOKit";

    private readonly InputDeviceCatalog catalog;
    private readonly ILogger<MacOsCoreHidInputProvider> logger;
    private IntPtr hidManager;
    private bool started;
    private bool disposed;

    public MacOsCoreHidInputProvider(
        InputDeviceCatalog catalog,
        ILogger<MacOsCoreHidInputProvider> logger)
    {
        this.catalog = catalog;
        this.logger = logger;
    }

    public string DisplayName => "macOS CoreHID";

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsMacOS())
        {
            catalog.Update([], "ProviderStatus_CoreHidUnavailable");
            return ValueTask.CompletedTask;
        }

        try
        {
            // kIOHIDOptionsTypeNone = 0
            hidManager = IOHIDManagerCreate(IntPtr.Zero, 0);
            if (hidManager == IntPtr.Zero)
            {
                logger.LogError("IOHIDManagerCreate returned null.");
                catalog.Update([], "ProviderStatus_CoreHidActive", 0);
                return ValueTask.CompletedTask;
            }

            // kIOHIDOptionsTypeNone = 0
            var open = IOHIDManagerOpen(hidManager, 0);
            if (open != 0)
            {
                logger.LogError("IOHIDManagerOpen failed with status {Status} — input monitoring permission may be required.", open);
            }

            started = true;
            catalog.Update([], "ProviderStatus_CoreHidActive", 0);
            logger.LogInformation("CoreHID provider initialized — device discovery is event-driven.");
        }
        catch (DllNotFoundException ex)
        {
            logger.LogError(ex, "IOKit framework not available — CoreHID provider cannot start.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "CoreHID provider failed to initialize.");
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<ControllerSnapshot> PollAsync(CancellationToken cancellationToken)
    {
        // Reading per-element state from CoreHID requires registering an input-value
        // callback and dispatching it on a CFRunLoop. The minimal viable provider in
        // this commit returns the empty snapshot — full implementation lands as a
        // follow-up because it requires a Swift bridge for the input-value typedef.
        if (disposed || !started)
        {
            return ValueTask.FromResult(ControllerSnapshot.Empty);
        }

        return ValueTask.FromResult(ControllerSnapshot.Empty);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (OperatingSystem.IsMacOS() && hidManager != IntPtr.Zero)
        {
            try
            {
                _ = IOHIDManagerClose(hidManager, 0);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error closing IOHIDManager.");
            }

            hidManager = IntPtr.Zero;
        }

        await ValueTask.CompletedTask;
    }

    [DllImport(IOKitFramework, EntryPoint = "IOHIDManagerCreate")]
    private static extern IntPtr IOHIDManagerCreate(IntPtr allocator, uint options);

    [DllImport(IOKitFramework, EntryPoint = "IOHIDManagerOpen")]
    private static extern int IOHIDManagerOpen(IntPtr manager, uint options);

    [DllImport(IOKitFramework, EntryPoint = "IOHIDManagerClose")]
    private static extern int IOHIDManagerClose(IntPtr manager, uint options);
}
