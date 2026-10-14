using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Runtime.Web.Overlay;
using GameFlow.Infrastructure.Theming;
using GameFlow.Infrastructure.Theming.Models;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

/// <summary>Tests for <see cref="OverlayThemeChoices"/>.</summary>
public sealed class OverlayThemeChoicesTests
{
    private readonly OverlayThemeChoices choices = new();

    private static InstalledTheme Theme(string id) =>
        new(id, id, "C:\\themes\\" + id, new ThemeDocument(), ControllerVisualStyle.PlayStation5);

    [Fact]
    public void GetUnknownSlotReturnsNull()
    {
        var theme = choices.Get("slot-1", physical: false);

        Assert.Null(theme);
    }

    [Fact]
    public void GetAfterSetReturnsTheme()
    {
        var white = Theme("dualsense-white");
        choices.Set("slot-1", physical: false, white);

        var theme = choices.Get("slot-1", physical: false);

        Assert.Same(white, theme);
    }

    [Fact]
    public void GetOtherSideReturnsItsOwnTheme()
    {
        var white = Theme("dualsense-white");
        var red = Theme("dualsense-red");
        choices.Set("slot-1", physical: false, white);
        choices.Set("slot-1", physical: true, red);

        var theme = choices.Get("slot-1", physical: true);

        Assert.Same(red, theme);
    }

    [Fact]
    public void SetNullThemeClearsChoice()
    {
        choices.Set("slot-1", physical: false, Theme("dualsense-white"));

        choices.Set("slot-1", physical: false, theme: null);

        Assert.Null(choices.Get("slot-1", physical: false));
    }

    [Fact]
    public void RemoveSlotClearsBothSides()
    {
        choices.Set("slot-1", physical: false, Theme("dualsense-white"));
        choices.Set("slot-1", physical: true, Theme("dualsense-red"));

        choices.Remove("slot-1");

        Assert.Null(choices.Get("slot-1", physical: false));
        Assert.Null(choices.Get("slot-1", physical: true));
    }
}
