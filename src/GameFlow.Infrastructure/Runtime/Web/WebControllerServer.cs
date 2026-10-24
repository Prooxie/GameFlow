using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Theming;
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
///
/// <para>
/// <b>Two directions, two routes.</b> <c>/</c> is the phone gamepad and
/// carries input INWARD. <c>/overlay</c> is the OBS browser source and
/// carries state OUTWARD — see
/// <see cref="Overlay.OverlayProgram"/>. They share this listener and
/// nothing else.
/// </para>
/// </summary>
public sealed class WebControllerServer(
    WebControllerHub hub,
    Overlay.OverlayFeed overlay,
    WindowsFirewallAccess firewall,
    ILogger<WebControllerServer> logger,
    IUserSettingsService? userSettings = null,
    PhoneRegistry? phones = null,
    LightingApi? lightingApi = null,
    GameFlow.Infrastructure.Localization.ILocalizationService? localization = null) : BackgroundService
{
    private readonly LightingApi? lightingApi = lightingApi;
    private readonly WebControllerHub hub = hub;
    private readonly PhoneRegistry? phones = phones;
    private readonly Overlay.OverlayFeed overlay = overlay;
    private readonly WindowsFirewallAccess firewall = firewall;
    private readonly ILogger<WebControllerServer> logger = logger;
    private HttpListener? listener;

    /// <summary>
    /// How often an overlay socket sends a frame. 60 Hz matches the
    /// dashboard's own tick and is comfortably past what a stream
    /// encoded at 60 fps can show; the socket is a few hundred bytes a
    /// frame, so the cost of being generous here is negligible.
    /// </summary>
    private static readonly TimeSpan OverlayFrameInterval = TimeSpan.FromMilliseconds(1000.0 / 60);

    /// <summary>
    /// Overlay frames between two theme checks: 15 frames at 60 Hz is 250 ms.
    /// A skin or type change in the app reaches OBS in this time, and the
    /// check stays out of most frames.
    /// </summary>
    private const int OverlayThemeCheckFrames = 15;

    /// <summary>
    /// Feedback is sent independently of incoming input. A phone at rest only
    /// sends a heartbeat once per second, which is far too slow for responsive
    /// rumble delivery.
    /// </summary>
    private static readonly TimeSpan PhoneFeedbackInterval = TimeSpan.FromMilliseconds(16);

    /// <summary>Hard bound for one phone input frame, including fragments.</summary>
    private const int MaxPhoneMessageBytes = 4096;
    private const int MaxConnections = 64;

    /// <summary>Default matches the port shown on the Dashboard's Web Controller card.</summary>
    public int Port { get; set; } = 8080;

    /// <summary>False until the listener is actually accepting; the Dashboard card reads this for its Running/Stopped state.</summary>
    public bool IsRunning { get; private set; }

    /// <summary>The URL to type into a phone browser, or null when not running.</summary>
    public string? ListenUrl { get; private set; }
    public bool AllowsRemote => userSettings?.Current.WebControllerAllowRemote == true;
    public string AccessToken => userSettings?.Current.WebAccessToken ?? string.Empty;
    public string LocalUrl => $"http://localhost:{Port}";
    public string SharedUrl => $"http://{GetLocalAddress()}:{Port}";

    /// <summary>
    /// The link one registered phone opens: the shared address, the access
    /// token, and the phone's seat, so it always becomes that phone. Empty
    /// while sharing is off or the server is not reachable from the network.
    /// </summary>
    public string PhoneLink(int seat) =>
        AllowsRemote && IsRunning && ListenUrl is { } listen
            && !listen.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            && seat is >= 0 and < WebControllerHub.MaxPads
            ? $"{SharedUrl}/?token={Uri.EscapeDataString(AccessToken)}&seat={seat}"
            : string.Empty;

    public async Task SetRemoteAccessAsync(bool enabled)
    {
        if (userSettings is null) { throw new InvalidOperationException("Network settings are unavailable."); }
        await userSettings.UpdateAsync(settings => settings with { WebControllerAllowRemote = enabled });
        if (enabled) { await EnsureFirewallRuleAsync(CancellationToken.None); }
    }

    public async Task ResetAccessTokenAsync()
    {
        if (userSettings is null) { throw new InvalidOperationException("Network settings are unavailable."); }
        var secret = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await userSettings.UpdateAsync(settings => settings with { WebAccessToken = secret });
    }

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

        // Off the accept loop: the COM query and netsh can take a second.
        if (AllowsRemote) { _ = EnsureFirewallRuleAsync(stoppingToken); }
        var requests = new List<Task>();

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

                requests.RemoveAll(task => task.IsCompleted);
                if (requests.Count >= MaxConnections)
                {
                    context.Response.StatusCode = 503;
                    context.Response.Close();
                    continue;
                }
                requests.Add(Task.Run(() => HandleContextAsync(context, stoppingToken), CancellationToken.None));
            }
        }
        finally
        {
            IsRunning = false;
            ListenUrl = null;
            listener.Close();
            try
            {
                await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                logger.LogDebug("Web controller: timed out waiting for connections to close.");
            }
        }
    }

    /// <summary>
    /// Opens the port to the LAN when GameFlow is elevated. See
    /// <see cref="WindowsFirewallAccess.EnsureInboundRuleAsync"/>.
    /// </summary>
    private async Task EnsureFirewallRuleAsync(CancellationToken stoppingToken)
    {
        try
        {
            await firewall.EnsureInboundRuleAsync(Port, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // shutting down — normal
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
            candidate.Close();
            logger.LogWarning(
                "Web controller: could not bind all interfaces on port {Port} ({Message}). " +
                "On Windows this needs a URL reservation. Click 'Allow through firewall' in Options, " +
                "or start GameFlow as administrator once. Falling back to localhost only.",
                Port, exception.Message);
        }

        var localOnly = new HttpListener();
        localOnly.Prefixes.Add($"http://localhost:{Port}/");
        try
        {
            localOnly.Start();
            ListenUrl = $"http://localhost:{Port}";
            logger.LogWarning(
                "Web controller: listening on {Url} — THIS PC ONLY. Phones on your network cannot reach it " +
                "until the URL reservation above is added and GameFlow restarts.", ListenUrl);
            return localOnly;
        }
        catch (HttpListenerException exception)
        {
            localOnly.Close();
            logger.LogError(exception, "Web controller: could not start on port {Port} at all.", Port);
            return null;
        }
    }

    private async Task HandleContextAsync(HttpListenerContext context, CancellationToken stoppingToken)
    {
        try
        {
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";

            // The lighting API answers before the page check below. That
            // check accepts the token from the query string, which is right
            // for a phone link and wrong for an API credential, and answers
            // with an HTML page a script cannot read. The API applies its
            // own, stricter rule (bearer header only, even on loopback).
            if (lightingApi is not null && LightingApi.IsApiPath(context.Request.Url?.AbsolutePath))
            {
                await HandleLightingApiAsync(context, stoppingToken);
                return;
            }

            if (!IsAuthorized(context))
            {
                context.Response.StatusCode = AllowsRemote ? 401 : 403;
                await WriteHtmlAsync(context, "Open the private connection link from GameFlow. Phone sharing must be enabled first.", stoppingToken);
                context.Response.Close();
                return;
            }

            if (!WebRequestPolicy.Allows(context.Request.Url, context.Request.Headers["Origin"], context.Request.LocalEndPoint.Address))
            {
                context.Response.StatusCode = 403;
                context.Response.Close();
                return;
            }

            if (context.Request.IsWebSocketRequest)
            {
                if (context.Request.Url?.AbsolutePath is not ("/ws" or "/overlay/ws"))
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }
                await HandleWebSocketAsync(context, stoppingToken);
                return;
            }

            var path = context.Request.Url?.AbsolutePath ?? "/";
            if (path is "/" or "/index.html")
            {
                await WriteHtmlAsync(context, WebControllerAssets.RenderControllerPage(localization), stoppingToken);
            }
            else if (path is "/overlay" or "/overlay/")
            {
                await WriteHtmlAsync(context, Overlay.OverlayAssets.RenderOverlayPage(localization), stoppingToken);
            }
            else if (path is "/overlay/asset")
            {
                await WriteOverlayAssetAsync(context, stoppingToken);
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

    private static async Task WriteHtmlAsync(HttpListenerContext context, string html, CancellationToken stoppingToken)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, stoppingToken);
    }

    /// <summary>
    /// Serves one image out of a compiled theme, addressed by INDEX.
    ///
    /// <para>
    /// There is deliberately no path parameter to sanitise here. The only
    /// files this can return are ones
    /// <see cref="Overlay.OverlayProgramBuilder"/> already resolved out of
    /// the requested theme, so the usual traversal question — can a
    /// caller walk out of the themes folder — cannot arise. An index the
    /// program does not have is a 404.
    /// </para>
    /// </summary>
    private async Task WriteOverlayAssetAsync(HttpListenerContext context, CancellationToken stoppingToken)
    {
        var query = context.Request.QueryString;
        var theme = overlay.ResolveTheme(new Overlay.OverlayFeed.Request(query["theme"], SlotId: null, Physical: false));
        if (theme is null || !int.TryParse(query["i"], out var index))
        {
            context.Response.StatusCode = 404;
            return;
        }

        var program = Overlay.OverlayProgramBuilder.Build(theme);
        if (index < 0 || index >= program.ImagePaths.Count)
        {
            context.Response.StatusCode = 404;
            return;
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(program.ImagePaths[index], stoppingToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The art was there when the theme compiled and is not now —
            // a pack uninstalled mid-stream. 404 so the page simply draws
            // without it, which is what it already does for art a pack
            // never shipped.
            logger.LogDebug(exception, "Overlay: asset {Index} of theme {Theme} could not be read.", index, theme.Id);
            context.Response.StatusCode = 404;
            return;
        }

        context.Response.ContentType = ContentTypeFor(program.ImagePaths[index]);
        context.Response.ContentLength64 = bytes.Length;
        // Art is immutable for the life of a compiled theme, and OBS
        // re-requests every image on each source reload.
        context.Response.Headers["Cache-Control"] = "public, max-age=86400";
        await context.Response.OutputStream.WriteAsync(bytes, stoppingToken);
    }

    private static string ContentTypeFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };

    private async Task HandleWebSocketAsync(HttpListenerContext context, CancellationToken stoppingToken)
    {
        if ((context.Request.Url?.AbsolutePath ?? "/") is "/overlay/ws")
        {
            await HandleOverlaySocketAsync(context, stoppingToken);
            return;
        }

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
        var clientId = context.Request.QueryString["client"];
        var requestedSeat = ParseSeat(context.Request.QueryString["seat"]);

        // A phone's own link claims its seat outright. A link for a phone
        // that has since been removed is refused, so a forgotten browser tab
        // cannot quietly come back as a controller. The general link takes
        // any seat no registered phone is waiting for, and that seat becomes
        // a registered phone from then on.
        if (requestedSeat is { } removedSeat && phones is not null && !phones.IsRegistered(removedSeat))
        {
            await RefuseAsync(socket, WebControllerProtocol.BuildRevoked(), "removed", stoppingToken);
            return;
        }

        var lease = requestedSeat is { } seat
            ? hub.ClaimSeat(seat, clientId)
            : hub.ClaimPad(clientId, phones?.Seats);
        var padIndex = lease.PadIndex;

        try
        {
            if (padIndex < 0)
            {
                logger.LogInformation("Web controller: a phone connected but all {Max} pads are in use.", WebControllerHub.MaxPads);
                await RefuseAsync(socket, WebControllerProtocol.BuildFull(), "full", stoppingToken);
                return;
            }

            phones?.EnsureRegistered(padIndex);

            // Tell the phone which pad it is before anything else.
            await SendAsync(socket, WebControllerProtocol.BuildPadAssignment(padIndex, phones?.NameOf(padIndex)), stoppingToken);
            logger.LogInformation("Web controller: phone connected as pad #{Pad}.", padIndex + 1);

            using var session = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            var receiveTask = ReceivePhoneInputAsync(socket, lease, session.Token);
            var feedbackTask = SendPhoneFeedbackAsync(socket, lease, session.Token, () => IsAuthorized(context));

            _ = await Task.WhenAny(receiveTask, feedbackTask);
            await session.CancelAsync();

            // Observe both tasks. Cancellation is the expected way the sibling
            // loop leaves after the first one detects a close or replacement.
            try
            {
                await Task.WhenAll(receiveTask, feedbackTask);
            }
            catch (OperationCanceledException) when (session.IsCancellationRequested)
            {
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
            if (lease.IsValid)
            {
                hub.ReleasePad(lease);
                logger.LogInformation("Web controller: pad #{Pad} disconnected.", padIndex + 1);
            }
            try { socket.Dispose(); } catch { /* already gone */ }
        }
    }

    /// <summary>A seat number from the query string, or null when absent or out of range.</summary>
    private static int? ParseSeat(string? value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seat)
            && seat is >= 0 and < WebControllerHub.MaxPads
            ? seat
            : null;

    /// <summary>Sends a terminal message and closes. The page shows it and stops reconnecting.</summary>
    private static async Task RefuseAsync(WebSocket socket, string message, string reason, CancellationToken cancellationToken)
    {
        try
        {
            await SendAsync(socket, message, cancellationToken);
            await socket.CloseAsync(WebSocketCloseStatus.PolicyViolation, reason, cancellationToken);
        }
        catch (WebSocketException)
        {
            // The browser left first — nothing to tell it.
        }
        finally
        {
            try { socket.Dispose(); } catch { /* already gone */ }
        }
    }

    /// <summary>
    /// Receives bounded, optionally-fragmented text frames. Ownership is
    /// checked on every update so a replaced socket exits instead of writing
    /// into its successor's pad.
    /// </summary>
    private async Task ReceivePhoneInputAsync(
        WebSocket socket,
        WebPadLease lease,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[MaxPhoneMessageBytes];

        while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            var bytesReceived = 0;
            WebSocketReceiveResult result;
            do
            {
                if (bytesReceived == buffer.Length)
                {
                    await socket.CloseAsync(
                        WebSocketCloseStatus.MessageTooBig,
                        $"input frames are limited to {MaxPhoneMessageBytes} bytes",
                        cancellationToken);
                    return;
                }

                result = await socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer, bytesReceived, buffer.Length - bytesReceived),
                    cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                bytesReceived += result.Count;
            }
            while (!result.EndOfMessage);

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(buffer, 0, bytesReceived);
            var snapshot = WebControllerProtocol.TryParseInput(json, lease.PadIndex, out var layout);
            if (snapshot is not null && !hub.UpdatePad(lease, snapshot, layout))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Drains feedback on its own cadence rather than waiting for another
    /// input frame. This makes a game-requested rumble start promptly even
    /// while the phone controls are untouched.
    /// </summary>
    private async Task SendPhoneFeedbackAsync(
        WebSocket socket,
        WebPadLease lease,
        CancellationToken cancellationToken,
        Func<bool> isAuthorized)
    {
        using var timer = new PeriodicTimer(PhoneFeedbackInterval);
        var sentName = phones?.NameOf(lease.PadIndex);
        while (socket.State == WebSocketState.Open
               && await timer.WaitForNextTickAsync(cancellationToken))
        {
            if (!hub.IsLeaseCurrent(lease) || !isAuthorized())
            {
                // Removed in the app: say so, so the page stops reconnecting
                // instead of retrying a link that will never work again.
                if (phones is not null && !phones.IsRegistered(lease.PadIndex))
                {
                    await SendAsync(socket, WebControllerProtocol.BuildRevoked(), cancellationToken);
                }
                return;
            }

            // A rename in the app reaches the phone's own header.
            var name = phones?.NameOf(lease.PadIndex);
            if (!string.Equals(name, sentName, StringComparison.Ordinal))
            {
                sentName = name;
                await SendAsync(socket, WebControllerProtocol.BuildPadAssignment(lease.PadIndex, name), cancellationToken);
            }

            while (hub.TryDequeueRumble(lease, out var rumble))
            {
                await SendAsync(
                    socket,
                    WebControllerProtocol.BuildRumble(rumble),
                    cancellationToken);
            }
        }
    }

    /// <summary>
    /// Streams one browser source. Sends the compiled theme once, then a
    /// frame per tick for as long as the source stays connected.
    ///
    /// <para>
    /// The loop never READS from the socket. An overlay is output-only,
    /// and giving the page no way to talk back is the cheapest way to be
    /// sure a browser source — pointed at a server that anyone on the LAN
    /// can reach — cannot influence the runtime.
    /// </para>
    ///
    /// <para>
    /// <b>The layout follows the app.</b> Every
    /// <see cref="OverlayThemeCheckFrames"/> frames the loop resolves the theme
    /// again. When the slot type or the dashboard skin changed, it sends a new
    /// program, and the page swaps the layout with no reload.
    /// </para>
    ///
    /// <para>
    /// Frames go out unconditionally rather than only on change. A
    /// dirty check would save bandwidth that is already negligible, and
    /// it would mean a source that connected during an idle moment sat
    /// blank until someone pressed a button.
    /// </para>
    /// </summary>
    private async Task HandleOverlaySocketAsync(HttpListenerContext context, CancellationToken stoppingToken)
    {
        WebSocketContext socketContext;
        try
        {
            socketContext = await context.AcceptWebSocketAsync(subProtocol: null);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Overlay: WebSocket upgrade failed.");
            return;
        }

        var query = context.Request.QueryString;
        var request = new Overlay.OverlayFeed.Request(
            query["theme"],
            query["slot"],
            // Virtual is the default: it is what the game receives, which
            // is what an input-display overlay is for. The physical pad
            // is a query away for anyone who wants the hardware instead.
            Physical: string.Equals(query["side"], "physical", StringComparison.OrdinalIgnoreCase),
            DeviceId: query["device"]);

        var socket = socketContext.WebSocket;
        try
        {
            var program = overlay.ResolveProgram(request);
            if (program is null)
            {
                await SendAsync(socket, Overlay.OverlayProtocol.BuildError(
                    localization?["OverlayNoLayout"] ?? "No layout is available. Open this panel in GameFlow and choose a skin."), stoppingToken);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "no layout", stoppingToken);
                return;
            }

            await SendProgramAsync(socket, program, stoppingToken);
            logger.LogInformation("Overlay: a browser source connected, drawing {Theme}.", program.ThemeId);

            // One symbol table for the life of the connection — rebinding
            // a snapshot is a field assignment, and this runs 60x/sec.
            var symbols = new ControllerStateSymbols();
            using var ticker = new PeriodicTimer(OverlayFrameInterval);
            var framesSinceThemeCheck = 0;

            while (socket.State == WebSocketState.Open && await ticker.WaitForNextTickAsync(stoppingToken))
            {
                if (!IsAuthorized(context)) { break; }
                if (++framesSinceThemeCheck >= OverlayThemeCheckFrames)
                {
                    framesSinceThemeCheck = 0;

                    // Null means the slot is gone. Keep the last layout: the
                    // controller goes neutral, which is better than a blank scene.
                    var latest = overlay.ResolveProgram(request);
                    if (latest is not null && !ReferenceEquals(latest, program))
                    {
                        logger.LogInformation("Overlay: layout changed from {Old} to {New}.", program.ThemeId, latest.ThemeId);
                        program = latest;
                        await SendProgramAsync(socket, program, stoppingToken);
                    }
                }

                var frame = Overlay.OverlayProtocol.BuildFrame(
                    program, symbols, overlay.Snapshot(request), overlay.LightColor(request));
                await SendAsync(socket, frame, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down — normal
        }
        catch (WebSocketException)
        {
            // OBS closed the source or reloaded the page — normal
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Overlay: session ended unexpectedly.");
        }
        finally
        {
            logger.LogInformation("Overlay: a browser source disconnected.");
            try { socket.Dispose(); } catch { /* already gone */ }
        }
    }

    /// <summary>Sends a compiled layout to the browser.</summary>
    private async Task SendProgramAsync(
        WebSocket socket,
        Overlay.OverlayProgram program,
        CancellationToken cancellationToken)
    {
        if (program.MissingImages.Count > 0)
        {
            logger.LogWarning(
                "Overlay: theme {Theme} references {Count} image(s) that are not on disk; they will not draw.",
                program.ThemeId, program.MissingImages.Count);
        }

        var labels = Enum.GetValues<GameFlow.Core.Enums.ButtonId>().ToDictionary(button => (int)button,
            button => localization?["ButtonId." + button] ?? button.ToString());
        string Label(string key, string fallback) => localization?[key] ?? fallback;
        string[] axes = [Label("StickId.Left", "Left stick"), Label("StickId.Right", "Right stick"),
            Label("OverlayLeftTrigger", "Left trigger"), Label("OverlayRightTrigger", "Right trigger"),
            Label("MouseInput.None", "Mouse movement")];
        await SendAsync(socket, Overlay.OverlayProtocol.BuildProgram(program, labels, axes), cancellationToken);
    }

    private static Task SendAsync(WebSocket socket, string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    /// <summary>
    /// Adapts one HTTP request to <see cref="LightingApi"/> and writes its
    /// answer. The body is read only for POST, and never past
    /// <see cref="LightingApi.MaxBodyBytes"/> + 1 — enough to tell an
    /// oversized body from a full one without buffering whatever a caller
    /// chooses to send.
    /// </summary>
    private async Task HandleLightingApiAsync(HttpListenerContext context, CancellationToken stoppingToken)
    {
        var request = context.Request;
        var response = context.Response;
        response.Headers["Cache-Control"] = "no-store";

        try
        {
            ReadOnlyMemory<byte> body = ReadOnlyMemory<byte>.Empty;
            var method = request.HttpMethod.ToUpperInvariant();
            var tooLarge = request.ContentLength64 > LightingApi.MaxBodyBytes;
            if (method == "POST" && request.HasEntityBody && !tooLarge)
            {
                var buffer = new byte[LightingApi.MaxBodyBytes + 1];
                var read = 0;
                int chunk;
                while (read < buffer.Length
                       && (chunk = await request.InputStream.ReadAsync(buffer.AsMemory(read), stoppingToken)) > 0)
                {
                    read += chunk;
                }

                body = buffer.AsMemory(0, read);
            }
            else if (tooLarge)
            {
                // Signalled to the policy as one byte past the limit.
                body = new byte[LightingApi.MaxBodyBytes + 1];
            }

            var answer = lightingApi!.Handle(
                new LightingApiRequest(
                    method,
                    request.Url?.AbsolutePath ?? "/",
                    request.QueryString["target"],
                    request.Headers["Authorization"],
                    request.ContentType,
                    IPAddress.IsLoopback(request.RemoteEndPoint.Address),
                    WebRequestPolicy.Allows(request.Url, request.Headers["Origin"], request.LocalEndPoint.Address),
                    body),
                AllowsRemote,
                AccessToken);

            response.StatusCode = answer.StatusCode;
            if (answer.ChallengeBearer)
            {
                response.Headers["WWW-Authenticate"] = "Bearer";
            }

            if (answer.Allow is { } allow)
            {
                response.Headers["Allow"] = allow;
            }

            if (answer.RetryAfterSeconds is { } retryAfter)
            {
                response.Headers["Retry-After"] = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (answer.Json is { } json)
            {
                var bytes = Encoding.UTF8.GetBytes(json);
                response.ContentType = "application/json; charset=utf-8";
                response.ContentLength64 = bytes.Length;
                await response.OutputStream.WriteAsync(bytes, stoppingToken);
            }

            if (answer.StatusCode >= 400 && answer.StatusCode != 429)
            {
                logger.LogDebug(
                    "Lighting API: {Method} {Path} answered {Status}.",
                    method, request.Url?.AbsolutePath, answer.StatusCode);
            }
        }
        finally
        {
            response.Close();
        }
    }

    private bool IsAuthorized(HttpListenerContext context) => WebAccessPolicy.IsAuthorized(
        IPAddress.IsLoopback(context.Request.RemoteEndPoint.Address), AllowsRemote, AccessToken, context.Request.QueryString["token"]);

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
