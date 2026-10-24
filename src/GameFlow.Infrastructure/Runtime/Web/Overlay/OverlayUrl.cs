namespace GameFlow.Infrastructure.Runtime.Web.Overlay;

/// <summary>
/// Builds the OBS browser-source URL for one dashboard surface.
///
/// <para>
/// One place for the query contract the overlay route reads (see
/// <see cref="OverlayFeed.Request"/>): <c>slot</c>, <c>device</c>,
/// <c>side=physical</c>, <c>theme</c> and the page's own <c>bg</c>. Every
/// dashboard panel copies its own link through here, so the link names
/// exactly the controller and side it was copied from — the old single
/// dashboard button always produced a bare <c>/overlay</c>, which shows
/// whichever controller happens to be first.
/// </para>
/// </summary>
public static class OverlayUrl
{
    /// <param name="baseUrl">The server's local address, e.g. <c>http://localhost:8080</c>.</param>
    /// <param name="slotId">Virtual controller, or null for a lone device.</param>
    /// <param name="deviceId">Lone physical device (a pinned panel), or null.</param>
    /// <param name="physical">The pad in the player's hands rather than the game's view.</param>
    /// <param name="background">
    /// The surface's background colour (<c>#RRGGBB</c>) to carry into OBS —
    /// a chroma-key colour picked on the dashboard keys the same way in the
    /// stream. Empty or null keeps the overlay transparent, which composites
    /// straight over a scene.
    /// </param>
    /// <param name="themeId">An explicit skin, or null to follow the dashboard's.</param>
    /// <param name="inputText">Show the active input names.</param>
    /// <param name="holdTimes">Show how long each active input has been held.</param>
    public static string Build(
        string baseUrl,
        string? slotId,
        string? deviceId,
        bool physical,
        string? background = null,
        string? themeId = null,
        bool inputText = false,
        bool holdTimes = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var query = new List<string>(5);
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            query.Add("device=" + Uri.EscapeDataString(deviceId));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(slotId))
            {
                query.Add("slot=" + Uri.EscapeDataString(slotId));
            }
            if (physical)
            {
                query.Add("side=physical");
            }
        }

        if (!string.IsNullOrWhiteSpace(themeId))
        {
            query.Add("theme=" + Uri.EscapeDataString(themeId));
        }

        if (TryNormalizeColor(background, out var color))
        {
            query.Add("bg=" + color);
        }
        if (inputText) { query.Add("text=1"); }
        if (holdTimes) { query.Add("holds=1"); }

        return baseUrl.TrimEnd('/') + "/overlay" + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty);
    }

    /// <summary>
    /// Accepts <c>#RRGGBB</c> or <c>#AARRGGBB</c> only, returned as bare hex
    /// (the page adds the <c>#</c>). Anything else — "Transparent", a theme
    /// default, a named colour — means no background parameter.
    /// </summary>
    private static bool TryNormalizeColor(string? value, out string color)
    {
        color = string.Empty;
        var text = value?.Trim().TrimStart('#') ?? string.Empty;
        if (text.Length is not (6 or 8) || !text.All(Uri.IsHexDigit))
        {
            return false;
        }

        color = text.Length == 8 ? text[2..] : text; // CSS hex is RRGGBBAA; drop a leading alpha instead
        return true;
    }
}
