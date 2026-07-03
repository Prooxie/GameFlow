using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Autofire.App.Converters;

/// <summary>
/// Maps "is this VK code in the live pressed-keys set?" to a brush.
/// Binding: pass <c>PressedKeysSet</c> as Value and the VK code (hex
/// digits, e.g. <c>57</c> for W) as <c>ConverterParameter</c>.
/// </summary>
public sealed class VkPressedToBrushConverter : IValueConverter
{
    public IBrush Active { get; set; } = new SolidColorBrush(Color.FromRgb(0x65, 0xD3, 0x84));
    public IBrush Idle   { get; set; } = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IReadOnlySet<int> pressed
            && parameter is string vkHex
            && int.TryParse(vkHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vk)
            && pressed.Contains(vk))
        {
            return Active;
        }
        return Idle;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
