using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GameFlow.App.ViewModels;
using Serilog;

namespace GameFlow.App.Views;

/// <summary>Sheet for connecting one phone. See <see cref="PhoneSheetViewModel"/>.</summary>
public partial class PhoneControllerWindow : Window
{
    public PhoneControllerWindow()
    {
        InitializeComponent();
        Opened += (_, _) => WindowSizing.Fit(this);
        Opened += OnOpened;
        Closed += OnClosed;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is PhoneSheetViewModel viewModel)
        {
            viewModel.Network.Refresh();
            _ = viewModel.Network.RefreshReachabilityAsync();
            viewModel.Refresh();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is PhoneSheetViewModel viewModel)
        {
            viewModel.Dispose();
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PhoneSheetViewModel { HasLink: true } viewModel ||
            GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            await clipboard.SetTextAsync(viewModel.Link);
        }
        catch (Exception exception)
        {
            // The link sits in a read-only box right beside the button, so a
            // clipboard briefly owned by another process still leaves it
            // selectable by hand.
            Log.Warning(exception, "Phone controller: copying the link failed.");
        }
    }
}
