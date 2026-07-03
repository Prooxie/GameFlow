using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;

namespace Autofire.App.ViewModels;

/// <summary>
/// One entry in the left menu column (PadForge-style): an icon + name +
/// click command that jumps to the right tab and selects the underlying
/// device or slot. The instance carries no state beyond its display
/// fields, so simple <see langword="get"/>-only properties work — no
/// INPC needed.
/// </summary>
public sealed class MenuColumnItemViewModel
{
    public string Id { get; }
    public string Name { get; }
    public string IconText { get; }
    public bool IsConnected { get; }
    public ICommand SelectCommand { get; }

    public MenuColumnItemViewModel(string id, string name, string iconText, bool isConnected, Action onSelect)
    {
        Id = id;
        Name = string.IsNullOrWhiteSpace(name) ? "(unnamed)" : name;
        IconText = iconText;
        IsConnected = isConnected;
        SelectCommand = new RelayCommand(onSelect);
    }
}
