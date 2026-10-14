using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace GameFlow.App.Views;

/// <summary>
/// The GameFlow logo mark as scalable vector art. Sizes to whatever box it
/// is given; see the AXAML for why it is drawn rather than referenced as
/// the app-icon bitmap.
/// </summary>
public partial class BrandMark : UserControl
{
    public BrandMark()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
