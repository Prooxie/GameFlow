using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using GameFlow.Core.Models;
using GameFlow.Core.Pipeline;
using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Runtime.Effects;
using GameFlow.Infrastructure.Runtime.Slots;

namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>One API request, reduced to what the policy needs. Built by the server from the HTTP context.</summary>
/// <param name="Method">HTTP method, upper case.</param>
/// <param name="Path">Absolute path, e.g. <c>/api/v1/lighting</c>.</param>
/// <param name="QueryTarget">The <c>target</c> query parameter, for DELETE.</param>
/// <param name="Authorization">The raw <c>Authorization</c> header.</param>
/// <param name="ContentType">The raw <c>Content-Type</c> header.</param>
/// <param name="IsLoopback">The caller is on this PC.</param>
/// <param name="OriginAllowed">The request passed <see cref="WebRequestPolicy.Allows"/>.</param>
/// <param name="Body">The request body, already capped at <see cref="LightingApi.MaxBodyBytes"/>.</param>
internal readonly record struct LightingApiRequest(
    string Method,
    string Path,
    string? QueryTarget,
    string? Authorization,
    string? ContentType,
    bool IsLoopback,
    bool OriginAllowed,
    ReadOnlyMemory<byte> Body);

/// <summary>What to send back. <see cref="Json"/> is null for an empty (204) response.</summary>
internal readonly record struct LightingApiResponse(
    int StatusCode,
    string? Json = null,
    bool ChallengeBearer = false,
    string? Allow = null,
    int? RetryAfterSeconds = null);

/// <summary>
/// The external lighting API: lets Home Assistant, a script, or a small
/// helper beside Wallpaper Engine set controller lights over HTTP.
///
/// <para>
/// <c>POST /api/v1/lighting</c> sets a colour, <c>DELETE</c> hands the
/// lights back, <c>GET /api/v1/lighting/targets</c> lists what can be
/// addressed. Requests land in <see cref="ExternalLightingStore"/>, which
/// the effect producer layers over the configured lighting until each
/// request's time-to-live runs out.
/// </para>
///
/// <para>
/// <b>Why it is this strict.</b> It is off unless the user turns it on;
/// it wants the access token as a bearer header even from this PC (see
/// <see cref="WebAccessPolicy.IsApiAuthorized"/>); it keeps the server's
/// Host/Origin check, so a web page in a browser cannot drive it; and it
/// sends no CORS headers, on purpose. Wallpaper Engine's web wallpapers
/// run as <c>file://</c> pages with an opaque origin, so the supported
/// route for them is a small local script or plugin that makes the call —
/// not loosening the origin rule for every page on the machine.
/// </para>
///
/// <para>
/// Kept apart from <see cref="WebControllerServer"/> and free of
/// <c>HttpListener</c> so the whole policy — auth, limits, parsing,
/// routing — is testable without opening a port.
/// </para>
/// </summary>
public sealed class LightingApi(
    ExternalLightingStore store,
    SlotRegistry slots,
    InputDeviceCatalog catalog,
    IUserSettingsService? userSettings = null,
    TimeProvider? timeProvider = null)
{
    /// <summary>Every API path starts with this.</summary>
    public const string PathPrefix = "/api/";

    public const string LightingPath = "/api/v1/lighting";
    public const string TargetsPath = "/api/v1/lighting/targets";

    /// <summary>Largest request body accepted. A lighting request is under 200 bytes.</summary>
    public const int MaxBodyBytes = 4096;

    /// <summary>
    /// Requests accepted per second, across all callers. Comfortably more
    /// than a 30 fps effect script needs, and low enough that a runaway
    /// loop cannot keep the server busy.
    /// </summary>
    public const int MaxRequestsPerSecond = 30;

    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly Lock rateGate = new();
    private long windowStartTicks;
    private int requestsInWindow;

    /// <summary>Whether the user has switched the API on.</summary>
    public bool IsEnabled => userSettings?.Current.ExternalLightingApiEnabled == true;

    /// <summary>True for any path this API owns, so the server can route it before its pages.</summary>
    public static bool IsApiPath(string? path) =>
        path is not null
        && (path.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, "/api", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Applies the whole policy to one request, in a fixed order: switched
    /// on, from an allowed origin, under the rate limit, authenticated,
    /// then routed.
    /// </summary>
    internal LightingApiResponse Handle(in LightingApiRequest request, bool allowsRemote, string accessToken)
    {
        if (!IsEnabled)
        {
            return Error(403, "The external lighting API is turned off. Turn it on in GameFlow: Options, Network & phones.");
        }

        if (!request.OriginAllowed)
        {
            return Error(403, "Requests from web pages are not accepted. Call the API from a script or service.");
        }

        if (!TryTakeRateToken())
        {
            return Error(429, $"Too many requests. The limit is {MaxRequestsPerSecond} per second.") with
            {
                RetryAfterSeconds = 1,
            };
        }

        if (!WebAccessPolicy.IsApiAuthorized(request.IsLoopback, allowsRemote, accessToken, request.Authorization))
        {
            return Error(
                401,
                request.IsLoopback || allowsRemote
                    ? "Send the access token from GameFlow as 'Authorization: Bearer <token>'."
                    : "Requests from other devices need network sharing turned on in GameFlow.") with
            {
                ChallengeBearer = true,
            };
        }

        var path = request.Path.TrimEnd('/');
        if (string.Equals(path, TargetsPath, StringComparison.OrdinalIgnoreCase))
        {
            return request.Method == "GET"
                ? new LightingApiResponse(200, BuildTargetsJson())
                : Error(405, "Use GET.") with { Allow = "GET" };
        }

        if (string.Equals(path, LightingPath, StringComparison.OrdinalIgnoreCase))
        {
            return request.Method switch
            {
                "POST" => HandleSet(request),
                "DELETE" => HandleClear(request.QueryTarget),
                _ => Error(405, "Use POST to set lighting or DELETE to release it.") with { Allow = "POST, DELETE" },
            };
        }

        return Error(404, "Unknown API path.");
    }

    private LightingApiResponse HandleSet(in LightingApiRequest request)
    {
        if (!IsJson(request.ContentType))
        {
            return Error(415, "Send the body as Content-Type: application/json.");
        }

        if (request.Body.Length > MaxBodyBytes)
        {
            return Error(413, $"The body is limited to {MaxBodyBytes} bytes.");
        }

        if (!TryParseSet(request.Body, out var parsed, out var problem))
        {
            return Error(400, problem);
        }

        if (!TryResolveTarget(parsed.Target, out var target))
        {
            return Error(404, $"No controller or slot called '{parsed.Target}'. GET {TargetsPath} lists them.");
        }

        store.Set(target, parsed.Request, parsed.TimeToLive);
        return new LightingApiResponse(204);
    }

    private LightingApiResponse HandleClear(string? queryTarget)
    {
        var name = string.IsNullOrWhiteSpace(queryTarget) ? "all" : queryTarget.Trim();
        if (!TryResolveTarget(name, out var target))
        {
            return Error(404, $"No controller or slot called '{name}'.");
        }

        _ = store.Clear(target);
        return new LightingApiResponse(204);
    }

    /// <summary>A parsed POST body.</summary>
    internal readonly record struct SetRequest(string Target, ExternalLightRequest Request, TimeSpan TimeToLive);

    /// <summary>
    /// Validates a POST body. Every field is checked and every rejection
    /// says which field and why: the caller is usually someone writing a
    /// Home Assistant YAML entry, and "400" alone sends them guessing.
    /// </summary>
    internal static bool TryParseSet(ReadOnlyMemory<byte> body, out SetRequest result, out string problem)
    {
        result = default;
        problem = string.Empty;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 4 });
        }
        catch (JsonException)
        {
            problem = "The body is not valid JSON.";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problem = "The body must be a JSON object.";
                return false;
            }

            var target = "all";
            if (root.TryGetProperty("target", out var targetElement))
            {
                if (targetElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(targetElement.GetString()))
                {
                    problem = "'target' must be a non-empty string: \"all\", a slot id or number, or a controller id.";
                    return false;
                }

                target = targetElement.GetString()!.Trim();
            }

            var mode = LightbarMode.Solid;
            if (root.TryGetProperty("mode", out var modeElement))
            {
                if (modeElement.ValueKind != JsonValueKind.String || !TryParseMode(modeElement.GetString(), out mode))
                {
                    problem = "'mode' must be one of: solid, breathing, strobe, rainbow, off.";
                    return false;
                }
            }

            var color = LightColor.Black;
            if (root.TryGetProperty("color", out var colorElement))
            {
                if (colorElement.ValueKind != JsonValueKind.String || !TryParseHexColor(colorElement.GetString(), out color))
                {
                    problem = "'color' must be a colour in the form \"#RRGGBB\".";
                    return false;
                }
            }
            else if (mode is LightbarMode.Solid or LightbarMode.Breathing or LightbarMode.Strobe)
            {
                problem = $"'color' is required for mode '{ModeName(mode)}'.";
                return false;
            }

            var brightness = 1f;
            if (root.TryGetProperty("brightness", out var brightnessElement))
            {
                if (brightnessElement.ValueKind != JsonValueKind.Number
                    || !brightnessElement.TryGetDouble(out var value)
                    || !double.IsFinite(value) || value < 0 || value > 1)
                {
                    problem = "'brightness' must be a number from 0 to 1.";
                    return false;
                }

                brightness = (float)value;
            }

            var ttl = ExternalLightingStore.DefaultTimeToLive;
            if (root.TryGetProperty("ttlMs", out var ttlElement))
            {
                var maximum = (long)ExternalLightingStore.MaximumTimeToLive.TotalMilliseconds;
                if (ttlElement.ValueKind != JsonValueKind.Number
                    || !ttlElement.TryGetInt64(out var milliseconds)
                    || milliseconds < 1 || milliseconds > maximum)
                {
                    problem = $"'ttlMs' must be a whole number of milliseconds from 1 to {maximum}.";
                    return false;
                }

                ttl = TimeSpan.FromMilliseconds(milliseconds);
            }

            result = new SetRequest(target, new ExternalLightRequest(mode, color, brightness), ttl);
            return true;
        }
    }

    /// <summary>
    /// Turns a target name into a store key: "all", a slot id, a 1-based
    /// slot number (what a person sees in the app), or the id of a pad
    /// assigned to a slot or known to the device catalog.
    /// </summary>
    internal bool TryResolveTarget(string name, out ExternalLightingTarget target)
    {
        target = ExternalLightingTarget.All;
        if (string.Equals(name, "all", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var allSlots = slots.GetSlots();
        var slot = allSlots.FirstOrDefault(s => string.Equals(s.Id, name, StringComparison.OrdinalIgnoreCase));
        if (slot is null
            && int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            slot = allSlots.FirstOrDefault(s => s.Index + 1 == number);
        }

        if (slot is not null)
        {
            target = new ExternalLightingTarget(ExternalLightingScope.Slot, slot.Id);
            return true;
        }

        var assigned = allSlots.SelectMany(s => s.InputDeviceIds)
            .FirstOrDefault(id => string.Equals(id, name, StringComparison.OrdinalIgnoreCase));
        if (assigned is not null || catalog.TryGetById(name, out _))
        {
            target = new ExternalLightingTarget(ExternalLightingScope.Device, assigned ?? name);
            return true;
        }

        return false;
    }

    /// <summary>
    /// The list a caller picks targets from: "all", every slot, and every
    /// pad assigned to a slot (lighting only reaches assigned pads).
    /// </summary>
    internal string BuildTargetsJson()
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteStartArray("targets");

            json.WriteStartObject();
            json.WriteString("id", "all");
            json.WriteString("kind", "all");
            json.WriteString("name", "All controllers");
            json.WriteEndObject();

            var seenDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allSlots = slots.GetSlots();
            foreach (var slot in allSlots)
            {
                json.WriteStartObject();
                json.WriteString("id", slot.Id);
                json.WriteString("kind", "slot");
                json.WriteString("name", string.IsNullOrWhiteSpace(slot.Name) ? $"Virtual controller {slot.Index + 1}" : slot.Name);
                json.WriteNumber("number", slot.Index + 1);
                json.WriteBoolean("enabled", slot.Enabled);
                json.WriteEndObject();
            }

            foreach (var slot in allSlots)
            {
                foreach (var deviceId in slot.InputDeviceIds)
                {
                    if (string.IsNullOrWhiteSpace(deviceId) || !seenDevices.Add(deviceId))
                    {
                        continue;
                    }

                    var known = catalog.TryGetById(deviceId, out var info) && info is not null;
                    json.WriteStartObject();
                    json.WriteString("id", deviceId);
                    json.WriteString("kind", "device");
                    json.WriteString("name", known ? info!.DisplayName : deviceId);
                    json.WriteString("slot", slot.Id);
                    json.WriteBoolean("connected", known && info!.IsConnected);
                    if (known && ControllerFeatureSet.Describe(info!.EffectiveFeatures) is { Count: > 0 } features)
                    {
                        json.WriteStartArray("features");
                        foreach (var feature in features)
                        {
                            json.WriteStringValue(feature);
                        }

                        json.WriteEndArray();
                    }

                    json.WriteEndObject();
                }
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    /// <summary>
    /// Fixed one-second window. Simple on purpose: the point is to stop a
    /// runaway loop, not to meter fairly between callers.
    /// </summary>
    private bool TryTakeRateToken()
    {
        var now = clock.GetUtcNow().UtcTicks;
        lock (rateGate)
        {
            if (now - windowStartTicks >= TimeSpan.TicksPerSecond || now < windowStartTicks)
            {
                windowStartTicks = now;
                requestsInWindow = 0;
            }

            if (requestsInWindow >= MaxRequestsPerSecond)
            {
                return false;
            }

            requestsInWindow++;
            return true;
        }
    }

    private static bool IsJson(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseMode(string? text, out LightbarMode mode)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "solid":
                mode = LightbarMode.Solid;
                return true;
            case "breathing":
                mode = LightbarMode.Breathing;
                return true;
            case "strobe":
                mode = LightbarMode.Strobe;
                return true;
            case "rainbow":
                mode = LightbarMode.Rainbow;
                return true;
            case "off":
                mode = LightbarMode.Off;
                return true;
            default:
                mode = LightbarMode.Solid;
                return false;
        }
    }

    private static string ModeName(LightbarMode mode) => mode.ToString().ToLowerInvariant();

    /// <summary>Exactly six hex digits, with or without a leading '#'. No alpha: a light has none.</summary>
    private static bool TryParseHexColor(string? text, out LightColor color)
    {
        color = LightColor.Black;
        var trimmed = text?.Trim().TrimStart('#') ?? string.Empty;
        return trimmed.Length == 6
            && trimmed.All(Uri.IsHexDigit)
            && LightColor.TryParse(trimmed, out color);
    }

    private static LightingApiResponse Error(int status, string message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("error", message);
            json.WriteEndObject();
        }

        return new LightingApiResponse(status, Encoding.UTF8.GetString(buffer.WrittenSpan));
    }
}
