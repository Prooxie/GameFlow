using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Autofire.App.Views;

public partial class ControlMappingWindow : Window
{
    public ControlMappingWindow()
    {
        InitializeComponent();
        Closed += OnClosed;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        Closed -= OnClosed;
    }
}
