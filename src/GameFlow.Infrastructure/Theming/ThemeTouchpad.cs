namespace GameFlow.Infrastructure.Theming;

/// <summary>
/// Where a theme's touchpad is, for drawing fingers on it. All geometry is
/// in theme-local pixels — the space the theme's own <c>x</c>, <c>y</c>,
/// <c>width</c> and <c>height</c> fields use.
/// </summary>
/// <param name="Art">
/// The touchpad image's rectangle, exactly as the theme places it. This is
/// what the hit tester and the press overlay use.
/// </param>
/// <param name="Surface">
/// Where a normalized touch maps: (0, 0) is its top-left corner, (1, 1) its
/// bottom-right. The visible part of <paramref name="Art"/>, measured from
/// the image's alpha channel, less the theme's <c>touchpadInset</c>.
/// </param>
/// <param name="Outline">
/// The pad's silhouette as a closed polygon. Fingers, trails and smoke are
/// clipped to it, so they take the shape of the pad — a DualSense
/// trapezoid, a DualShock 4 rounded rectangle — instead of spilling over
/// its rectangle onto the controller body.
/// </param>
/// <param name="IsMeasured">
/// True when <paramref name="Surface"/> and <paramref name="Outline"/> came
/// from the art's alpha channel. False when the art could not be read, and
/// they are the image rectangle and a rounded rectangle fitted to it.
/// </param>
public sealed record ThemeTouchpad(
    ThemeRegion Art,
    ThemeRegion Surface,
    IReadOnlyList<ThemePoint> Outline,
    bool IsMeasured)
{
    /// <summary>
    /// The theme point under a touch at normalized (<paramref name="x"/>,
    /// <paramref name="y"/>) — the SDL convention, 0 at the left and top.
    /// Clamped to the surface, because a contact is always on the pad.
    /// </summary>
    public ThemePoint Map(double x, double y) =>
        Surface.PointAt(Clamp01(x), Clamp01(y));

    private static double Clamp01(double value) =>
        double.IsNaN(value) ? 0 : Math.Clamp(value, 0d, 1d);
}
