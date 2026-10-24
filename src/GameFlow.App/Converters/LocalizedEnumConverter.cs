using System.Globalization;
using Avalonia.Data.Converters;

namespace GameFlow.App.Converters;

public sealed class LocalizedEnumConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum option ? UiTextResources.EnumValue(option) : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
