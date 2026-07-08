namespace GameFlow.App.ViewModels;

/// <summary>
/// One live controller panel on the dashboard, backed by a slot. Wraps a
/// physical/virtual pair of <see cref="ControllerVisualStateViewModel"/>
/// instances the shell pump feeds the slot's latest snapshots into every
/// tick — a genuine side-by-side comparison per slot, at the same size
/// and style as the top-level pair, rather than a small virtual-only card.
/// </summary>
public sealed class DashboardControllerPanelViewModel : ViewModelBase
{
    public DashboardControllerPanelViewModel(
        string slotId, string title,
        ControllerVisualStateViewModel physicalVisual,
        ControllerVisualStateViewModel virtualVisual,
        string virtualBadgeLabel)
    {
        SlotId = slotId;
        this.title = title;
        PhysicalVisual = physicalVisual;
        VirtualVisual = virtualVisual;
        VirtualBadgeLabel = virtualBadgeLabel;
    }

    public string SlotId { get; }

    private string title;
    public string Title { get => title; set => SetProperty(ref title, value); }

    private string lightColor = "#00000000";
    /// <summary>Hex colour of the slot's lightbar (#AARRGGBB), transparent when off.</summary>
    public string LightColor { get => lightColor; set => SetProperty(ref lightColor, value); }

    /// <summary>This slot's physical (input) side of the comparison.</summary>
    public ControllerVisualStateViewModel PhysicalVisual { get; }

    /// <summary>This slot's virtual (output) side of the comparison.</summary>
    public ControllerVisualStateViewModel VirtualVisual { get; }

    /// <summary>
    /// "VIRTUAL" (localized), set once from the shell's current language
    /// when this panel is created. Marks the emitted device clearly —
    /// it's real hardware to every other application on the system, just
    /// not to this one (see the hardware-signature filtering that hides
    /// it from GameFlow's own input list).
    /// </summary>
    public string VirtualBadgeLabel { get; }
}
