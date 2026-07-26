using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>
/// Serves the browser gamepad to any device on the local network and
/// receives its input over a WebSocket. No app to install on the phone:
/// the page is a single self-contained HTML document with no external
/// requests, so it loads over the LAN with no internet access at all.
///
/// <para>
/// Uses <see cref="HttpListener"/> from the BCL rather than pulling in
/// ASP.NET Core — this project has no web dependencies today and one
/// static page plus one socket endpoint doesn't justify adding the
/// whole framework.
/// </para>
///
/// <para>
/// <b>Binding and firewalls.</b> Listening on all interfaces
/// (<c>http://+:port/</c>) needs an admin-registered URL ACL on
/// Windows; when that fails the server retries on
/// <c>http://localhost:port/</c> so it still works for local testing,
/// and logs clearly that phones on the network won't reach it. That
/// distinction matters: silently serving only localhost would look
/// identical to "my phone can't connect" with no explanation.
/// </para>
/// </summary>
public sealed class WebControllerServer(
    WebControllerHub hub,
    ILogger<WebControllerServer> logger) : BackgroundService
{
    private readonly WebControllerHub hub = hub;
    private readonly ILogger<WebControllerServer> logger = logger;
    private HttpListener? listener;

    /// <summary>Default matches the port shown on the Dashboard's Web Controller card.</summary>
    public int Port { get; set; } = 8080;

    /// <summary>False until the listener is actually accepting; the Dashboard card reads this for its Running/Stopped state.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>The URL to type into a phone browser, or null when not running.</summary>
    public string? ListenUrl { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!HttpListener.IsSupported)
        {
            logger.LogWarning("Web controller: HttpListener is unavailable on this platform; server not started.");
            return;
        }

        listener = TryStartListener();
        if (listener is null)
        {
            return;
        }

        IsRunning = true;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().WaitAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break; // listener stopped from under us
                }

                // Each connection runs independently: one phone's slow
                // network must never stall the accept loop for everyone else.
                _ = Task.Run(() => HandleContextAsync(context, stoppingToken), CancellationToken.None);
            }
        }
        finally
        {
            IsRunning = false;
            ListenUrl = null;
            listener.Close();
        }
    }

    private HttpListener? TryStartListener()
    {
        // All interfaces first — that's the whole point, phones on Wi-Fi.
        var candidate = new HttpListener();
        candidate.Prefixes.Add($"http://+:{Port}/");
        try
        {
            candidate.Start();
            ListenUrl = $"http://{GetLocalAddress()}:{Port}";
            logger.LogInformation("Web controller: running on {Url} — open it in any browser on this network.", ListenUrl);
            return candidate;
        }
        catch (HttpListenerException exception)
        {
            logger.LogWarning(
                "Web controller: could not bind all interfaces on port {Port} ({Message}). " +
                "On Windows this usually needs an admin URL ACL: " +
                "netsh http add urlacl url=http://+:{Port}/ user=Everyone. Falling back to localhost only.",
                Port, exception.Message, Port);
        }

        var localOnly = new HttpListener();
        localOnly.Prefixes.Add($"http://localhost:{Port}/");
        try
        {
            localOnly.Start();
            ListenUrl = $"http://localhost:{Port}";
            logger.LogWarning(
                "Web controller: listening on {Url} — THIS PC ONLY. Phones on your network cannot reach it " +
                "until the URL ACL above is added.", ListenUrl);
            return localOnly;
        }
        catch (HttpListenerException exception)
        {
            logger.LogError(exception, "Web controller: could not start on port {Port} at all.", Port);
            return null;
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken stoppingToken)
    {
        try
        {
            if (context.Request.IsWebSocketRequest)
            {
                await HandleWebSocketAsync(context, stoppingToken);
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (path is "/" or "/index.html")
            {
                var bytes = Encoding.UTF8.GetBytes(WebControllerAssets.ControllerPage);
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, stoppingToken);
            }
            else
            {
                context.Response.StatusCode = 404;
            }
            context.Response.Close();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Web controller: request handling failed.");
            try { context.Response.Abort(); } catch { /* already gone */ }
        }
    }

    private async Task HandleWebSocketAsync(HttpListenerContext context, CancellationToken stoppingToken)
    {
        WebSocketContext socketContext;
        try
        {
            socketContext = await context.AcceptWebSocketAsync(subProtocol: null);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Web controller: WebSocket upgrade failed.");
            return;
        }

        var socket = socketContext.WebSocket;
        var padIndex = hub.ClaimPad();

        try
        {
            // Tell the phone which pad it is (or that we're full) before anything else.
            await SendAsync(socket, WebControllerProtocol.BuildPadAssignment(padIndex), stoppingToken);
            if (padIndex < 0)
            {
                logger.LogInformation("Web controller: a phone connected but all {Max} pads are in use.", WebControllerHub.MaxPads);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "full", stoppingToken);
                return;
            }

            logger.LogInformation("Web controller: phone connected as pad #{Pad}.", padIndex + 1);

            var buffer = new byte[4096];
            while (socket.State == WebSocketState.Open && !stoppingToken.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), stoppingToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    var snapshot = WebControllerProtocol.TryParseInput(json, padIndex);
                    if (snapshot is not null)
                    {
                        hub.UpdatePad(padIndex, snapshot); // reference type — no .Value after the null check
                    }
                }

                // Drain any rumble the pipeline queued for this phone.
                while (hub.TryDequeueRumble(padIndex, out var rumble))
                {
                    await SendAsync(socket, WebControllerProtocol.BuildRumble(rumble), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down — normal
        }
        catch (WebSocketException)
        {
            // phone walked out of range / closed abruptly — normal
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Web controller: session for pad #{Pad} ended unexpectedly.", padIndex + 1);
        }
        finally
        {
            if (padIndex >= 0)
            {
                hub.ReleasePad(padIndex);
                logger.LogInformation("Web controller: pad #{Pad} disconnected.", padIndex + 1);
            }
            try { socket.Dispose(); } catch { /* already gone */ }
        }
    }

    private static Task SendAsync(WebSocket socket, string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    /// <summary>
    /// Best-effort LAN address to show the user. Opening a UDP socket to
    /// a public address doesn't send anything — it just makes the OS
    /// pick the interface it WOULD route through, which is the one the
    /// phone can reach. More reliable than taking the first NIC in the
    /// list, which is often a virtual adapter.
    /// </summary>
    private static string GetLocalAddress()
    {
        try
        {
            using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            probe.Connect("8.8.8.8", 65530);
            return probe.LocalEndPoint is IPEndPoint endpoint ? endpoint.Address.ToString() : "localhost";
        }
        catch (SocketException)
        {
            return "localhost";
        }
    }
}
