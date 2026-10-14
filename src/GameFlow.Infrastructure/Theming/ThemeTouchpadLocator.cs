using GameFlow.Infrastructure.Theming.Flee;
using GameFlow.Infrastructure.Theming.Models;

namespace GameFlow.Infrastructure.Theming;

/// <summary>
/// Finds the touchpad rectangle of a theme, for the finger markers.
/// </summary>
/// <remarks>
/// <para>
/// Most themes have no <c>trailpad</c> node. The dashboard and the OBS overlay
/// then draw each finger inside the touchpad rectangle. Both use this class,
/// so both put a finger at the same place.
/// </para>
/// <para>
/// The touchpad is the image under a <c>showhide</c> node whose first variable is
/// <c>touch_center:click</c> — the same rule that the click-to-map hit tester uses.
/// Node offsets add up from the root. Rotation and slider movement are ignored,
/// as in the hit tester. When a theme has more than one such image, the last one
/// in paint order wins, because it is drawn on top.
/// </para>
/// </remarks>
public static class ThemeTouchpadLocator
{
    /// <summary>Theme variable for a touchpad click.</summary>
    private const string TouchpadClickVariable = "touch_center:click";

    /// <summary>The touchpad rectangle, or null when the theme shows no touchpad.</summary>
    public static ThemeRegion? Find(ThemeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        ThemeRegion? found = null;
        foreach (var node in document.Children)
        {
            Walk(node, 0, 0, isUnderTouchpad: false, ref found);
        }

        return found;
    }

    private static void Walk(ThemeNode node, double offsetX, double offsetY, bool isUnderTouchpad, ref ThemeRegion? found)
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
            found = new ThemeRegion(
                x - (image.Center ? image.Width / 2 : 0),
                y - (image.Center ? image.Height / 2 : 0),
                image.Width,
                image.Height);
        }

        foreach (var child in node.Children)
        {
            Walk(child, x, y, isUnderTouchpad, ref found);
        }
    }
}
