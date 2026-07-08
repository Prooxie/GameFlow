using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameFlow.Infrastructure.Overlay;

/// <summary>
/// Serves the controller overlay over HTTP/WebSocket on localhost. The
/// page at <c>/</c> opens a WebSocket to <c>/ws</c>, which streams the
/// current <see cref="RuntimeSnapshot.VirtualSnapshot"/> as JSON at a
/// fixed cadence. Useful as an OBS browser source and as a live sandbox
/// for verifying which inputs the OS/drivers register.
///
/// <para>Fully self-contained and defensive: any listener failure (port
/// in use, access denied) is logged and the overlay simply stays off
/// rather than taking down the app.</para>
/// </summary>
public sealed class OverlayServer(
    RuntimeSnapshotStore snapshotStore,
    IOptions<OverlayOptions> options,
    ILogger<OverlayServer> logger) : BackgroundService
{
    private static readonly TimeSpan BroadcastInterval = TimeSpan.FromMilliseconds(33); // ~30 Hz

    private readonly RuntimeSnapshotStore snapshotStore = snapshotStore;
    private readonly OverlayOptions options = options.Value;
    private readonly ILogger<OverlayServer> logger = logger;

    private static readonly ButtonId[] BroadcastButtons =
        Enum.GetValues<ButtonId>().Where(b => b != ButtonId.None).ToArray();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            logger.LogInformation("Controller overlay disabled.");
            return;
        }

        if (!HttpListener.IsSupported)
        {
            logger.LogWarning("HttpListener not supported on this platform; overlay disabled.");
            return;
        }

        HttpListener listener;
        try
        {
            listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{options.Port}/");
            listener.Start();
            logger.LogInformation("Controller overlay listening at http://localhost:{Port}/", options.Port);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to start controller overlay on port {Port}; overlay disabled.", options.Port);
            return;
        }

        // Closing the listener unblocks the pending GetContextAsync.
        using var registration = stoppingToken.Register(() =>
        {
            try { listener.Close(); }
            catch (Exception exception) { logger.LogDebug(exception, "Overlay listener close error."); }
        });

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (Exception) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (HttpListenerException exception)
                {
                    logger.LogDebug(exception, "Overlay listener stopped accepting.");
                    break;
                }

                _ = HandleContextAsync(context, stoppingToken);
            }
        }
        finally
        {
            try { listener.Close(); } catch { /* already closing */ }
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            if (context.Request.IsWebSocketRequest)
            {
                if (!string.Equals(context.Request.Url?.AbsolutePath, "/ws", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }

                var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                await StreamAsync(wsContext.WebSocket, cancellationToken);
            }
            else
            {
                ServeStatic(context);
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Overlay request handling error.");
            try { context.Response.Abort(); } catch { /* ignore */ }
        }
    }

    private static void ServeStatic(HttpListenerContext context)
    {
        var path = context.Request.Url?.AbsolutePath ?? "/";
        if (path is "/" or "/index.html")
        {
            var bytes = Encoding.UTF8.GetBytes(OverlayAssets.Html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.Close();
            return;
        }

        context.Response.StatusCode = 404;
        context.Response.Close();
    }

    private async Task StreamAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var json = BuildStateJson(snapshotStore.Current.VirtualSnapshot);
                var payload = Encoding.UTF8.GetBytes(json);
                await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);

                // Drain any client close handshake without blocking the cadence.
                if (socket.State != WebSocketState.Open)
                {
                    break;
                }

                await Task.Delay(BroadcastInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutting down
        }
        catch (WebSocketException)
        {
            // client vanished
        }
        finally
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "Overlay socket close error.");
                }
            }
            socket.Dispose();
        }
    }

    private static string BuildStateJson(ControllerSnapshot s)
    {
        var buttons = new Dictionary<string, bool>(BroadcastButtons.Length);
        foreach (var button in BroadcastButtons)
        {
            buttons[button.ToString()] = s.IsPressed(button);
        }

        var dto = new
        {
            device = s.DeviceName,
            lx = s.LeftStick.X,
            ly = s.LeftStick.Y,
            rx = s.RightStick.X,
            ry = s.RightStick.Y,
            lt = s.LeftTrigger,
            rt = s.RightTrigger,
            buttons,
        };
        return JsonSerializer.Serialize(dto);
    }
}
