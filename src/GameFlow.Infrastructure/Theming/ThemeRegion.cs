namespace GameFlow.Infrastructure.Theming;

/// <summary>
/// An axis-aligned rectangle in theme-local pixels — the space that the
/// <c>x</c>, <c>y</c>, <c>width</c> and <c>height</c> fields of theme.json use.
/// </summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width, more than zero.</param>
/// <param name="Height">Height, more than zero.</param>
public readonly record struct ThemeRegion(double X, double Y, double Width, double Height)
{
    /// <summary>
    /// The point at normalized (<paramref name="u"/>, <paramref name="v"/>):
    /// (0, 0) is the top-left corner and (1, 1) the bottom-right. Values
    /// outside 0..1 are not clamped, so smoke that drifts off a finger can
    /// leave the region and be clipped rather than pile up at its edge.
    /// </summary>
    public ThemePoint PointAt(double u, double v) => new(X + (Width * u), Y + (Height * v));

    /// <summary>This region shrunk by <paramref name="inset"/>, never below zero size.</summary>
    public ThemeRegion Deflate(ThemeInset inset)
    {
        var width = Math.Max(0, Width - inset.Left - inset.Right);
        var height = Math.Max(0, Height - inset.Top - inset.Bottom);
        return new ThemeRegion(X + inset.Left, Y + inset.Top, width, height);
    }
}

/// <summary>
/// Distances in from each edge of a <see cref="ThemeRegion"/>, in theme units.
/// Same order as Avalonia's <c>Thickness</c>.
/// </summary>
/// <param name="Left">From the left edge.</param>
/// <param name="Top">From the top edge.</param>
/// <param name="Right">From the right edge.</param>
/// <param name="Bottom">From the bottom edge.</param>
public readonly record struct ThemeInset(double Left, double Top, double Right, double Bottom)
{
    /// <summary>True when every edge is zero.</summary>
    public bool IsEmpty => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
}
