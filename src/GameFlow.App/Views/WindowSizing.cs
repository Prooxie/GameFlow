using Avalonia.Controls;

namespace GameFlow.App.Views;

/// <summary>Keeps dialogs usable on laptop displays and scaled secondary monitors.</summary>
internal static class WindowSizing
{
    internal static void Fit(Window window)
    {
        var screen = window.Owner is { } owner ? window.Screens.ScreenFromWindow(owner) : window.Screens.ScreenFromWindow(window);
        screen ??= window.Screens.Primary;
        if (screen is null) { return; }
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        var width = screen.WorkingArea.Width / scaling * 0.92;
        var height = screen.WorkingArea.Height / scaling * 0.92;
        window.MinWidth = Math.Min(window.MinWidth, width);
        window.MinHeight = Math.Min(window.MinHeight, height);
        window.MaxWidth = Math.Min(window.MaxWidth, width);
        window.MaxHeight = Math.Min(window.MaxHeight, height);
        if (!double.IsNaN(window.Width)) { window.Width = Math.Min(window.Width, width); }
        if (!double.IsNaN(window.Height)) { window.Height = Math.Min(window.Height, height); }
    }
}
