namespace GameFlow.Infrastructure.Theming;

/// <summary>
/// An axis-aligned rectangle in theme-local pixels — the space that the
/// <c>x</c>, <c>y</c>, <c>width</c> and <c>height</c> fields of theme.json use.
/// </summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width, more than zero.</param>
/// <param name="Height">Height, more than zero.</param>
public readonly record struct ThemeRegion(double X, double Y, double Width, double Height);
