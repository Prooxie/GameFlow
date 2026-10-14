using GameFlow.App.ViewModels;
using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Runtime.Web.Overlay;
using GameFlow.Infrastructure.Theming;
using GameFlow.Infrastructure.Theming.Models;
using Xunit;

namespace GameFlow.Infrastructure.Tests.ViewModels;

/// <summary>Tests for <see cref="DashboardControllerPanelViewModel"/>.</summary>
public sealed class DashboardControllerPanelViewModelTests
{
    private const string SlotId = "slot-1";

    private readonly OverlayThemeChoices choices = new();
    private readonly ControllerVisualStateViewModel physicalVisual = new(_ => { });
    private readonly ControllerVisualStateViewModel virtualVisual = new(_ => { });

    private DashboardControllerPanelViewModel CreatePanel() =>
        new(SlotId, "Pad", physicalVisual, virtualVisual, "VIRTUAL", choices);

    private static InstalledTheme Theme(string id) =>
        new(id, id, "C:\\themes\\" + id, new ThemeDocument(), ControllerVisualStyle.PlayStation5);

    [Fact]
    public void VirtualSkinChangeSetsOverlayChoice()
    {
        using var panel = CreatePanel();
        var red = Theme("dualsense-red");

        virtualVisual.SelectedThemeVariant = red;

        Assert.Same(red, choices.Get(SlotId, physical: false));
    }

    [Fact]
    public void PhysicalSkinChangeSetsPhysicalOverlayChoice()
    {
        using var panel = CreatePanel();
        var white = Theme("dualsense-white");

        physicalVisual.SelectedThemeVariant = white;

        Assert.Same(white, choices.Get(SlotId, physical: true));
        Assert.Null(choices.Get(SlotId, physical: false));
    }

    /// <summary>
    /// A removed panel must not pin its last skin. The overlay then follows the slot type again.
    /// </summary>
    [Fact]
    public void DisposeRemovesOverlayChoice()
    {
        var panel = CreatePanel();
        virtualVisual.SelectedThemeVariant = Theme("dualsense-red");

        panel.Dispose();

        Assert.Null(choices.Get(SlotId, physical: false));
    }

    [Fact]
    public void DisposeStopsLaterSkinChanges()
    {
        var panel = CreatePanel();
        panel.Dispose();

        virtualVisual.SelectedThemeVariant = Theme("dualsense-red");

        Assert.Null(choices.Get(SlotId, physical: false));
    }
}
