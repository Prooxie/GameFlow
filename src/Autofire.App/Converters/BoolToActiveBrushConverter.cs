using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Autofire.App.Converters;

/// <summary>True → <see cref="Active"/> brush, otherwise <see cref="Idle"/>.</summary>
public sealed class BoolToActiveBrushConverter : IValueConverter
{
    public IBrush Active { get; set; } = new SolidColorBrush(Color.FromRgb(0x65, 0xD3, 0x84));
    public IBrush Idle   { get; set; } = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b && b ? Active : Idle;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
