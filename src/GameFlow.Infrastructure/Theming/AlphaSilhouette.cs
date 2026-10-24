namespace GameFlow.Infrastructure.Theming;

/// <summary>
/// A point in theme-local or image pixels.
/// </summary>
/// <param name="X">Horizontal position, left origin.</param>
/// <param name="Y">Vertical position, top origin.</param>
public readonly record struct ThemePoint(double X, double Y);

/// <summary>
/// The visible shape of a piece of art, measured from its alpha channel:
/// the tight bounds of what is actually drawn, and an outline polygon
/// around it. Both are in the image's own pixels.
/// </summary>
/// <remarks>
/// <para>
/// Exists for the touchpad. The finger markers used to be stretched over
/// the touchpad image's whole rectangle, which only works when the art
/// fills its rectangle. A DualSense pad is a trapezoid: the bottom corners
/// of its rectangle are some fifty theme pixels outside the pad, so a
/// finger in a bottom corner drew on the controller body. And any pack
/// whose pad art has transparent padding, or a soft shadow, would shift
/// and shrink every finger by that margin.
/// </para>
/// <para>
/// "Visible" means at least half as opaque as the most opaque pixel in the
/// image. Half, because an anti-aliased edge crosses 50% coverage exactly
/// where the drawn shape's edge is; relative to the peak, because the
/// shipped pad overlays are a translucent fill (alpha 128) inside an opaque
/// stroke, and a pack that ships only a translucent fill would otherwise
/// have no visible pixels at all. Faint shadows and glows fall below it.
/// </para>
/// <para>
/// The outline is the row-wise hull: for every row, from the first visible
/// pixel to the last. A pad overlay is convex across each row — trapezoid,
/// rounded rectangle, circle — and the hull makes a hollow outline-only
/// overlay clip the same as a filled one, which is what a touch surface
/// needs. It is then simplified so a 300-row image yields a few dozen
/// points rather than six hundred.
/// </para>
/// </remarks>
public sealed class AlphaSilhouette
{
    /// <summary>
    /// How far, in image pixels, the simplified outline may stray from the
    /// row hull. Under a pixel: a theme draws the pad at or below its
    /// native size, so this never shows.
    /// </summary>
    private const double SimplifyTolerance = 0.6;

    private AlphaSilhouette(int imageWidth, int imageHeight, ThemeRegion bounds, IReadOnlyList<ThemePoint> outline)
    {
        ImageWidth = imageWidth;
        ImageHeight = imageHeight;
        Bounds = bounds;
        Outline = outline;
    }

    /// <summary>Width of the measured image, in pixels.</summary>
    public int ImageWidth { get; }

    /// <summary>Height of the measured image, in pixels.</summary>
    public int ImageHeight { get; }

    /// <summary>Tight bounds of the visible pixels, in image pixels.</summary>
    public ThemeRegion Bounds { get; }

    /// <summary>
    /// Closed outline around the visible pixels, in image pixels, clockwise
    /// on screen starting at the top-right. The last point connects back to
    /// the first.
    /// </summary>
    public IReadOnlyList<ThemePoint> Outline { get; }

    /// <summary>The silhouette of a fully opaque image: its own rectangle.</summary>
    public static AlphaSilhouette Opaque(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        return new AlphaSilhouette(
            width,
            height,
            new ThemeRegion(0, 0, width, height),
            [new(width, 0), new(width, height), new(0, height), new(0, 0)]);
    }

    /// <summary>
    /// Measures an alpha channel. Null when no pixel is visible, so the
    /// caller keeps the image rectangle rather than collapsing to nothing.
    /// </summary>
    /// <param name="alpha">One byte per pixel, row-major.</param>
    /// <param name="width">Image width in pixels.</param>
    /// <param name="height">Image height in pixels.</param>
    public static AlphaSilhouette? FromAlpha(ReadOnlySpan<byte> alpha, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        if (alpha.Length < (long)width * height)
        {
            throw new ArgumentException("The alpha channel is smaller than the image.", nameof(alpha));
        }

        byte peak = 0;
        foreach (var value in alpha[..(width * height)])
        {
            if (value > peak) { peak = value; }
        }

        if (peak == 0)
        {
            return null;
        }

        var threshold = (byte)Math.Max(1, (peak + 1) / 2);

        // First and last visible pixel per row; -1 for a row with none.
        var left = new int[height];
        var right = new int[height];
        int top = -1, bottom = -1, minLeft = width, maxRight = 0;

        for (var y = 0; y < height; y++)
        {
            var row = alpha.Slice(y * width, width);
            var first = -1;
            for (var x = 0; x < width; x++)
            {
                if (row[x] >= threshold) { first = x; break; }
            }

            left[y] = first;
            if (first < 0)
            {
                continue;
            }

            var last = first;
            for (var x = width - 1; x > first; x--)
            {
                if (row[x] >= threshold) { last = x; break; }
            }

            right[y] = last + 1;
            if (top < 0) { top = y; }
            bottom = y + 1;
            minLeft = Math.Min(minLeft, first);
            maxRight = Math.Max(maxRight, last + 1);
        }

        // Rows are sampled through their centres, and the first and last
        // rows also contribute their outer edge, so the outline reaches the
        // bounds exactly instead of stopping half a pixel inside them.
        // Clockwise in screen coordinates: down the right side, back along
        // the bottom, up the left side, and the closing edge is the top.
        var rightSide = new List<ThemePoint> { new(right[top], top) };
        for (var y = top; y < bottom; y++)
        {
            if (left[y] >= 0) { rightSide.Add(new(right[y], y + 0.5)); }
        }
        rightSide.Add(new(right[bottom - 1], bottom));

        var leftSide = new List<ThemePoint> { new(left[bottom - 1], bottom) };
        for (var y = bottom - 1; y >= top; y--)
        {
            if (left[y] >= 0) { leftSide.Add(new(left[y], y + 0.5)); }
        }
        leftSide.Add(new(left[top], top));

        var outline = Simplify(rightSide);
        outline.AddRange(Simplify(leftSide));

        return new AlphaSilhouette(
            width,
            height,
            new ThemeRegion(minLeft, top, maxRight - minLeft, bottom - top),
            outline);
    }

    /// <summary>
    /// Douglas–Peucker simplification of an open polyline. Keeps both
    /// endpoints, so the chain still meets its neighbour exactly.
    /// </summary>
    private static List<ThemePoint> Simplify(List<ThemePoint> points)
    {
        if (points.Count <= 2)
        {
            return [.. points];
        }

        var keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        var stack = new Stack<(int Start, int End)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (start, end) = stack.Pop();
            var farthest = -1;
            var farthestDistance = SimplifyTolerance;
            for (var i = start + 1; i < end; i++)
            {
                var distance = DistanceToSegment(points[i], points[start], points[end]);
                if (distance > farthestDistance)
                {
                    farthest = i;
                    farthestDistance = distance;
                }
            }

            if (farthest < 0)
            {
                continue;
            }

            keep[farthest] = true;
            stack.Push((start, farthest));
            stack.Push((farthest, end));
        }

        var result = new List<ThemePoint>();
        for (var i = 0; i < points.Count; i++)
        {
            if (keep[i]) { result.Add(points[i]); }
        }

        return result;
    }

    private static double DistanceToSegment(ThemePoint point, ThemePoint start, ThemePoint end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= 0)
        {
            return Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2));
        }

        var t = Math.Clamp((((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / lengthSquared, 0, 1);
        var nearestX = start.X + (t * dx);
        var nearestY = start.Y + (t * dy);
        return Math.Sqrt(Math.Pow(point.X - nearestX, 2) + Math.Pow(point.Y - nearestY, 2));
    }
}
