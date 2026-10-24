using System.Collections.Concurrent;
using GameFlow.Infrastructure.Theming.Flee;
using GameFlow.Infrastructure.Theming.Models;

namespace GameFlow.Infrastructure.Theming;

/// <summary>
/// Finds the touchpad of a theme, for the finger markers.
/// </summary>
/// <remarks>
/// <para>
/// Most themes have no <c>trailpad</c> node. The dashboard and the OBS overlay
/// then draw each finger inside the touchpad. Both use this class, so both put
/// a finger at the same place and clip it to the same shape.
/// </para>
/// <para>
/// The touchpad is the image under a <c>showhide</c> node whose first variable is
/// <c>touch_center:click</c> — the same rule that the click-to-map hit tester uses.
/// Node offsets add up from the root. Rotation and slider movement are ignored,
/// as in the hit tester; no shipped theme rotates its touchpad or any of its
/// ancestors. When a theme has more than one such image, the last one in paint
/// order wins, because it is drawn on top.
/// </para>
/// <para>
/// Fingers used to be stretched over that image's whole rectangle. That is
/// only right when the art fills its rectangle, and a touchpad rarely does:
/// the DualSense pad is a trapezoid whose bottom corners sit about fifty
/// theme pixels inside its rectangle, so a finger on the physical edge was
/// drawn on the controller body, and nothing stopped the smoke drifting off
/// it from spilling further. So the surface is now measured from the art's
/// alpha channel (<see cref="AlphaSilhouette"/>), once per image file, and
/// the silhouette comes back as an outline to clip every finger to.
/// </para>
/// </remarks>
public static class ThemeTouchpadLocator
{
    /// <summary>Theme variable for a touchpad click.</summary>
    private const string TouchpadClickVariable = "touch_center:click";

    /// <summary>
    /// Corner radius of the fallback outline, as a fraction of the shorter
    /// side, for art that cannot be read. Real pads are rounded; a square
    /// corner would let a finger's glow show past where the pad visibly ends.
    /// </summary>
    private const double FallbackCornerFraction = 0.12;

    /// <summary>Segments per quarter-circle in the fallback outline.</summary>
    private const int FallbackCornerSegments = 6;

    /// <summary>
    /// Measured silhouettes, keyed on the resolved file and its write time.
    /// Shared by every theme: the DualSense and DualShock 4 colour variants
    /// each reference one touchpad image, so six themes cost one decode.
    /// Null is cached too, so an unreadable image is tried once.
    /// </summary>
    private static readonly ConcurrentDictionary<(string Path, DateTime Written), AlphaSilhouette?> Silhouettes = new();

    /// <summary>The touchpad surface rectangle, or null when the theme shows no touchpad.</summary>
    /// <remarks>Shorthand for <see cref="Locate"/>'s <see cref="ThemeTouchpad.Surface"/>.</remarks>
    public static ThemeRegion? Find(ThemeDocument document) => Locate(document)?.Surface;

    /// <summary>
    /// The touchpad, or null when the theme shows none. Reads the touchpad
    /// image from disk the first time it is asked about, so call it off the
    /// UI thread; later calls for the same image are a dictionary lookup.
    /// </summary>
    public static ThemeTouchpad? Locate(ThemeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        (ImageNode Image, ThemeRegion Rect)? found = null;
        foreach (var node in document.Children)
        {
            Walk(node, 0, 0, isUnderTouchpad: false, ref found);
        }

        if (found is not { } pad)
        {
            return null;
        }

        var art = pad.Rect;
        var silhouette = Measure(pad.Image.ImagePath, document);
        var inset = document.TouchpadInset;

        if (silhouette is null)
        {
            return new ThemeTouchpad(art, art.Deflate(inset), RoundedOutline(art), IsMeasured: false);
        }

        // Image pixels to theme units: the theme may draw the art at any
        // size, and the measurement is of the file.
        var scaleX = art.Width / silhouette.ImageWidth;
        var scaleY = art.Height / silhouette.ImageHeight;
        ThemePoint ToTheme(ThemePoint p) => new(art.X + (p.X * scaleX), art.Y + (p.Y * scaleY));

        var bounds = silhouette.Bounds;
        var topLeft = ToTheme(new ThemePoint(bounds.X, bounds.Y));
        var surface = new ThemeRegion(topLeft.X, topLeft.Y, bounds.Width * scaleX, bounds.Height * scaleY);

        return new ThemeTouchpad(
            art,
            surface.Deflate(inset),
            [.. silhouette.Outline.Select(ToTheme)],
            IsMeasured: true);
    }

    private static void Walk(
        ThemeNode node, double offsetX, double offsetY, bool isUnderTouchpad, ref (ImageNode Image, ThemeRegion Rect)? found)
    {
        var x = offsetX + node.X;
        var y = offsetY + node.Y;

        if (node is ShowHideNode show && FleeVariables.FirstName(show.Input) is { } binding)
        {
            // A nested showhide sets its own control, as in the hit tester.
            isUnderTouchpad = string.Equals(binding.Trim(), TouchpadClickVariable, StringComparison.OrdinalIgnoreCase);
        }

        if (isUnderTouchpad && node is ImageNode { Width: > 0, Height: > 0 } image)
        {
            found = (image, new ThemeRegion(
                x - (image.Center ? image.Width / 2 : 0),
                y - (image.Center ? image.Height / 2 : 0),
                image.Width,
                image.Height));
        }

        foreach (var child in node.Children)
        {
            Walk(child, x, y, isUnderTouchpad, ref found);
        }
    }

    /// <summary>
    /// The silhouette of the touchpad art, or null when there is no file to
    /// read (an in-memory theme, a missing or unreadable image) or no pixel
    /// in it is visible.
    /// </summary>
    private static AlphaSilhouette? Measure(string imagePath, ThemeDocument document)
    {
        var resolved = ThemeAssetResolver.Resolve(imagePath, document.BaseDirectory, document.ThemesRootDirectory);
        if (resolved is null)
        {
            return null;
        }

        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(resolved);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        return Silhouettes.GetOrAdd((resolved, written), static key =>
        {
            if (!PngAlphaReader.TryRead(key.Path, out var width, out var height, out var alpha))
            {
                return null;
            }

            return alpha is null
                ? AlphaSilhouette.Opaque(width, height)
                : AlphaSilhouette.FromAlpha(alpha, width, height);
        });
    }

    /// <summary>A rounded rectangle filling <paramref name="rect"/>, clockwise from the top-right.</summary>
    private static List<ThemePoint> RoundedOutline(ThemeRegion rect)
    {
        var radius = Math.Min(rect.Width, rect.Height) * FallbackCornerFraction;
        var points = new List<ThemePoint>(4 * (FallbackCornerSegments + 1));

        // Corner centres in clockwise order with the angle each arc starts
        // at: top-right from -90°, bottom-right from 0°, and so on.
        (double Cx, double Cy, double Start)[] corners =
        [
            (rect.X + rect.Width - radius, rect.Y + radius, -90),
            (rect.X + rect.Width - radius, rect.Y + rect.Height - radius, 0),
            (rect.X + radius, rect.Y + rect.Height - radius, 90),
            (rect.X + radius, rect.Y + radius, 180),
        ];

        foreach (var (cx, cy, start) in corners)
        {
            for (var i = 0; i <= FallbackCornerSegments; i++)
            {
                var angle = (start + (90.0 * i / FallbackCornerSegments)) * Math.PI / 180;
                points.Add(new ThemePoint(cx + (radius * Math.Cos(angle)), cy + (radius * Math.Sin(angle))));
            }
        }

        return points;
    }
}
