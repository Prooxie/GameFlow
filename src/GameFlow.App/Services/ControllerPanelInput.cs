using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace GameFlow.App.Services;

internal static class ControllerPanelInput
{
    public static bool IsControlInteraction(object? source)
    {
        for (var visual = source as Visual; visual is not null; visual = visual.GetVisualParent())
        {
            if (visual is ComboBox or Button or TextBox or Slider or NumericUpDown or Thumb) { return true; }
        }
        return false;
    }
}
