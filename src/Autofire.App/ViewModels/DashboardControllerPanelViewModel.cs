namespace Autofire.App.ViewModels;

/// <summary>
/// One live controller panel on the dashboard, backed by a slot. Wraps a
/// <see cref="ControllerVisualStateViewModel"/> that the shell pump feeds
/// the slot's latest virtual snapshot, so each running controller shows
/// its own live state.
/// </summary>
public sealed class DashboardControllerPanelViewModel : ViewModelBase
{
    public DashboardControllerPanelViewModel(string slotId, string title, ControllerVisualStateViewModel visual)
    {
        SlotId = slotId;
        this.title = title;
        Visual = visual;
    }

    public string SlotId { get; }

    private string title;
    public string Title { get => title; set => SetProperty(ref title, value); }

    private string lightColor = "#00000000";
    /// <summary>Hex colour of the slot's lightbar (#AARRGGBB), transparent when off.</summary>
    public string LightColor { get => lightColor; set => SetProperty(ref lightColor, value); }

    public ControllerVisualStateViewModel Visual { get; }
}
