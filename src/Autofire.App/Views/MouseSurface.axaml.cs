using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Autofire.App.Views;

public partial class MouseSurface : UserControl
{
    public MouseSurface()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
