using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GameFlow.App.ViewModels;

namespace GameFlow.App.Views;

/// <summary>Hosts the keyboard &amp; mouse controller map editor.</summary>
public partial class KeyboardMouseMapWindow : Window
{
    public KeyboardMouseMapWindow()
    {
        InitializeComponent();
        Opened += (_, _) => WindowSizing.Fit(this);
        Closed += (_, _) => (DataContext as KeyboardMouseMapEditorViewModel)?.Dispose();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
