using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Theming.Flee;
using GameFlow.Infrastructure.Theming.Models;

namespace GameFlow.Infrastructure.Theming;

/// <summary>Repairs layouts from the original imported GameFlow asset packs.</summary>
internal static class ThemeCompatibility
{
    public static ThemeDocument Normalize(ThemeDocument document, ControllerVisualStyle style)
    {
        var children = document.Children.Select(node => Repair(node, document, style)).ToList();
        if (style == ControllerVisualStyle.PlayStation4 && document.Width == 1466 && ThemeTouchpadLocator.Find(document) is null)
        {
            children.Add(new ShowHideNode
            {
                Input = FleeParser.Parse("touch_center:click"),
                Children = [new ImageNode { X = 492, Y = 150, Width = 482, Height = 289,
                    ImagePath = @"\ds4\Image Assets\DS4-Touchpad-Cick.png" }]
            });
        }
        return document with { Children = children };
    }

    private static ThemeNode Repair(ThemeNode node, ThemeDocument document, ControllerVisualStyle style)
    {
        var children = node.Children.Select(child => Repair(child, document, style)).ToArray();
        if (style == ControllerVisualStyle.PlayStation4 && document.Width == 1466 && node is ImageNode image &&
            image.ImagePath.EndsWith("DS4-Touchpad-Cick.png", StringComparison.OrdinalIgnoreCase))
        { return image with { X = 492, Y = 150, Width = 482, Height = 289, Center = false, Children = children }; }
        // These imported packs placed the stick sprite at a guessed offset.
        // The native base image uses the same coordinate space as its sprite.
        if (node is SliderNode slider && document.Width is 1545 or 1534 &&
            style is ControllerVisualStyle.Xbox360 or ControllerVisualStyle.XboxSeries)
        {
            var input = FleeVariables.FirstName(slider.InputX) ?? string.Empty;
            var left = input.Contains("stick_left", StringComparison.OrdinalIgnoreCase);
            var right = input.Contains("stick_right", StringComparison.OrdinalIgnoreCase);
            if (left || right)
            {
                var x = style == ControllerVisualStyle.Xbox360 ? (left ? 207 : 897) : (left ? 286 : 906);
                var y = style == ControllerVisualStyle.Xbox360 ? (left ? 380 : 605) : (left ? 333 : 561);
                return slider with { X = x, Y = y, Children = children };
            }
        }
        return node with { Children = children };
    }

    public static string DisplayName(ThemeDocument document, ControllerVisualStyle style)
    {
        var name = document.Name.Trim();
        if (style == ControllerVisualStyle.PlayStation4)
        {
            name = name.Replace("DualShock 4 (DS4 V2 ", "DualShock 4 v2 (", StringComparison.OrdinalIgnoreCase)
                .Replace("DualShock 4 V1", "DualShock 4 v1", StringComparison.OrdinalIgnoreCase);
        }
        if (name.Equals("Black", StringComparison.OrdinalIgnoreCase) || name.Equals("Midnight Black", StringComparison.OrdinalIgnoreCase))
        {
            return style switch
            {
                ControllerVisualStyle.Xbox360 => "Xbox 360 (Black)",
                ControllerVisualStyle.XboxOne => "Xbox One (Black)",
                ControllerVisualStyle.XboxSeries => "Xbox Series X (Black)",
                ControllerVisualStyle.PlayStation5 => "DualSense (Midnight Black)",
                _ => name
            };
        }
        return name;
    }
}
