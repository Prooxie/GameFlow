using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GameFlow.Core.Enums;

namespace GameFlow.App.Converters;

/// <summary>Decodes imported input artwork once for each resource used by the UI.</summary>
/// <remarks>Call this converter and dispose its images on the UI thread.</remarks>
public sealed class InputAssetImageConverter : IValueConverter, IDisposable
{
    private const string ResourceRoot = "avares://GameFlow/Assets/InputAssets/";
    private readonly Dictionary<string, Bitmap> images = new(StringComparer.Ordinal);

    /// <summary>Gets a cached button prompt or controller family icon.</summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = value switch
        {
            ButtonId button => ButtonPath(button),
            ControllerVisualStyle style => IconPath(style),
            _ => null
        };

        if (path is null)
        {
            return null;
        }

        if (images.TryGetValue(path, out var image))
        {
            return image;
        }

        using var stream = AssetLoader.Open(new Uri(ResourceRoot + path));
        image = new Bitmap(stream);
        images.Add(path, image);
        return image;
    }

    /// <summary>Rejects reverse image conversion.</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException("Input artwork conversion supports display only.");

    /// <summary>Releases all decoded images when the application exits.</summary>
    public void Dispose()
    {
        foreach (var image in images.Values)
        {
            image.Dispose();
        }

        images.Clear();
    }

    /// <summary>Maps buttons to the shared positional prompt artwork.</summary>
    private static string? ButtonPath(ButtonId button)
    {
        if (button == ButtonId.Touchpad)
        {
            return "Prompts/PlayStation/ps5-Touchpad Click.png";
        }

        var name = button switch
        {
            ButtonId.None => null,
            ButtonId.South => "shared-positional prompt South",
            ButtonId.East => "shared-positional prompt East",
            ButtonId.West => "shared-positional prompt West",
            ButtonId.North => "shared-positional prompt North",
            ButtonId.LeftShoulder => "shared-L1",
            ButtonId.RightShoulder => "shared-R1",
            ButtonId.LeftTriggerButton => "shared-L2",
            ButtonId.RightTriggerButton => "shared-R2",
            ButtonId.Back => "shared-Select",
            ButtonId.Start => "shared-Start",
            ButtonId.Guide => "shared-Guide",
            ButtonId.LeftStick => "shared-L3 Click",
            ButtonId.RightStick => "shared-R3 Click",
            ButtonId.DpadUp => "shared-D-PAD Up",
            ButtonId.DpadDown => "shared-D-PA Down",
            ButtonId.DpadLeft => "shared-D-PAD Left",
            ButtonId.DpadRight => "shared-D-PAD Right",
            ButtonId.Paddle1 => "shared-L4",
            ButtonId.Paddle2 => "shared-R4",
            ButtonId.Paddle3 => "shared-L5",
            ButtonId.Paddle4 => "shared-R5",
            ButtonId.Misc1 => "shared-M1",
            _ => null
        };

        return name is null ? null : $"Prompts/Shared/{name}.png";
    }

    /// <summary>Maps controller styles to the imported controller type icons.</summary>
    private static string IconPath(ControllerVisualStyle style)
    {
        var name = style switch
        {
            ControllerVisualStyle.Xbox360 => "Xbox 360 Controller",
            ControllerVisualStyle.Xbox or ControllerVisualStyle.XboxOne or ControllerVisualStyle.XboxSeries =>
                "Xbox Wireless Controller",
            ControllerVisualStyle.PlayStation3 => "DualShock 3 Controller",
            ControllerVisualStyle.PlayStation4 => "DualShock 4 Controller",
            ControllerVisualStyle.PlayStation5 => "DualSense Controller",
            ControllerVisualStyle.NintendoSwitch => "Switch Pro Controller",
            ControllerVisualStyle.SteamController => "Steam Controller",
            ControllerVisualStyle.SteamControllerV2 => "Steam Controller Chell",
            ControllerVisualStyle.SteamDeck => "Steam Deck",
            ControllerVisualStyle.Arcade => "Arcade Stick",
            _ => "Generic Controller"
        };

        return $"Icons/Controller Type/SVG/VSCView {name}.png";
    }
}
