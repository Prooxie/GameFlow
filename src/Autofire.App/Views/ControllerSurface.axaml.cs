using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Autofire.App.Views;

public partial class ControllerSurface : UserControl
{
    public ControllerSurface()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
