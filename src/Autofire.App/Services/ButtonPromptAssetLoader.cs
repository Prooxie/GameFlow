using System.Collections.Concurrent;
using Autofire.Core.Enums;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Serilog;

namespace Autofire.App.Services;

/// <summary>
/// Optional helper that resolves button-prompt glyph PNGs from the
/// <see href="https://github.com/AL2009man/Gamepad-Prompt-Asset-Pack">AL2009man
/// Gamepad-Prompt-Asset-Pack</see> (MIT licensed, sister project to
/// the controller-overlay pack).
///
/// <para>
/// Convention:
/// <c>avares://Autofire.App/Assets/ButtonPrompts/&lt;style&gt;/&lt;key&gt;.png</c>
/// where <c>&lt;style&gt;</c> is one of <c>xbox</c>, <c>playstation4</c>,
/// <c>playstation5</c> and <c>&lt;key&gt;</c> is a stable button identifier
/// like <c>"south"</c>, <c>"east"</c>, <c>"l1"</c>, <c>"r2"</c>, etc.
/// </para>
///
/// <para>
/// When a glyph is missing, the loader returns <see langword="null"/>.
/// The caller (typically a XAML data template) is expected to fall
/// back to the existing text label — e.g. show the letter
/// <c>"A"</c> instead of an Xbox A-button glyph PNG.
/// </para>
///
/// <para>
/// Results are cached in-process: button-prompt icons are read on
/// every binding refresh and decoding the same PNG hundreds of times
/// per second would be wasteful. The cache key is
/// <c>(style, glyphKey)</c>; cache entries live for the duration of
/// the process.
/// </para>
///
/// <para>
/// This loader is provided as foundation for a follow-up roadmap pass.
/// Today no XAML binding consumes it — the existing controller surface
/// continues to render plain text labels inside button overlays. When
/// the prompt-icon pass lands, each TextBlock currently bound to e.g.
/// <c>SouthLegend</c> ("A" / "✕") will be paired with an Image bound to
/// <see cref="TryLoad(ControllerVisualStyle, string)"/> for that key.
/// </para>
/// </summary>
public static class ButtonPromptAssetLoader
{
    /// <summary>
    /// Process-wide cache of resolved bitmaps. <see langword="null"/>
    /// values are NOT cached — a missing file might be added by the
    /// user mid-session if they unzip the asset pack while the app is
    /// running.
    /// </summary>
    private static readonly ConcurrentDictionary<(ControllerVisualStyle Style, string Key), Bitmap> Cache = new();

    /// <summary>
    /// Tries to load the prompt glyph for <paramref name="style"/> +
    /// <paramref name="glyphKey"/>. Returns <see langword="null"/> when
    /// no file is installed for the combination.
    /// </summary>
    /// <param name="style">The controller layout the prompt belongs to.</param>
    /// <param name="glyphKey">
    /// Stable identifier for the button being prompted. Values follow
    /// the controller-snapshot lower-case convention: <c>"south"</c>,
    /// <c>"east"</c>, <c>"west"</c>, <c>"north"</c>, <c>"l1"</c>,
    /// <c>"r1"</c>, <c>"l2"</c>, <c>"r2"</c>, <c>"share"</c>,
    /// <c>"options"</c>, <c>"guide"</c>, <c>"l3"</c>, <c>"r3"</c>,
    /// <c>"dpad-up"</c>, <c>"dpad-down"</c>, <c>"dpad-left"</c>,
    /// <c>"dpad-right"</c>.
    /// </param>
    public static Bitmap? TryLoad(ControllerVisualStyle style, string glyphKey)
    {
        if (string.IsNullOrWhiteSpace(glyphKey))
        {
            return null;
        }

        var stylePath = ResolveStylePath(style);
        if (stylePath is null)
        {
            return null;
        }

        var cacheKey = (style, glyphKey);
        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var uri = new Uri($"avares://Autofire.App/Assets/ButtonPrompts/{stylePath}/{glyphKey}.png");

        try
        {
            using var stream = AssetLoader.Open(uri);
            var bitmap = new Bitmap(stream);
            Cache[cacheKey] = bitmap;
            Log.Debug(
                "Loaded button-prompt glyph {Style}/{GlyphKey} from {Uri}.",
                style,
                glyphKey,
                uri);
            return bitmap;
        }
        catch (FileNotFoundException)
        {
            // Expected — asset pack not installed for this style yet.
            return null;
        }
        catch (Exception exception)
        {
            Log.Warning(
                exception,
                "Could not load button-prompt glyph {Style}/{GlyphKey} from {Uri}.",
                style,
                glyphKey,
                uri);
            return null;
        }
    }

    /// <summary>
    /// Maps a style to the directory segment used in the resource URI.
    /// Returns <see langword="null"/> for styles that have no glyph
    /// pack (today: <see cref="ControllerVisualStyle.None"/> and
    /// <see cref="ControllerVisualStyle.Auto"/>).
    /// </summary>
    private static string? ResolveStylePath(ControllerVisualStyle style) => style switch
    {
        ControllerVisualStyle.Xbox => "xbox",
        ControllerVisualStyle.PlayStation4 => "playstation4",
        ControllerVisualStyle.PlayStation5 => "playstation5",
        _ => null,
    };

    /// <summary>
    /// Drops every cached bitmap. Useful in tests or after the user
    /// installs / replaces asset files at runtime.
    /// </summary>
    public static void ClearCache()
    {
        foreach (var kv in Cache.ToArray())
        {
            if (Cache.TryRemove(kv.Key, out var bitmap))
            {
                bitmap.Dispose();
            }
        }
    }
}
