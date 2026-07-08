using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GameFlow.App.Views;

public partial class KeyboardSurface : UserControl
{
    public KeyboardSurface()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
