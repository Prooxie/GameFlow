using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Runtime.Slots;
using GameFlow.Infrastructure.Runtime.Templates;
using GameFlow.Infrastructure.Runtime.Web.Overlay;
using GameFlow.Infrastructure.Theming;
using GameFlow.Infrastructure.Theming.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

/// <summary>Tests for <see cref="OverlayFeed"/>.</summary>
public sealed class OverlayFeedTests : IDisposable
{
    private readonly string slotsFile = Path.Combine(Path.GetTempPath(), "gameflow-overlay-feed-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly OverlayThemeChoices choices = new();
    private readonly SlotRegistry slots;
    private readonly OverlayFeed feed;
    private readonly string slotId;

    public OverlayFeedTests()
    {
        slots = new SlotRegistry(NullLogger<SlotRegistry>.Instance, slotsFile);
        slotId = slots.CreateSlot(VirtualControllerKind.Xbox360)!.Id;

        // An empty registry has no default theme for any style. Only a choice can give a theme.
        feed = new OverlayFeed(new ThemeRegistry(), slots, new SlotSnapshotStore(), choices);
    }

    public void Dispose() => File.Delete(slotsFile);

    private static InstalledTheme Theme(string id) =>
        new(id, id, "C:\\themes\\" + id, new ThemeDocument(), ControllerVisualStyle.PlayStation5);

    [Fact]
    public void ResolveThemeSlotWithChoiceReturnsChosenTheme()
    {
        var red = Theme("dualsense-red");
        choices.Set(slotId, physical: false, red);

        var theme = feed.ResolveTheme(new OverlayFeed.Request(ThemeId: null, slotId, Physical: false));

        Assert.Same(red, theme);
    }

    /// <summary>
    /// A bare overlay URL shows the first slot. It must follow that slot's dashboard skin too.
    /// </summary>
    [Fact]
    public void ResolveThemeNoSlotInUrlReturnsFirstSlotChoice()
    {
        var red = Theme("dualsense-red");
        choices.Set(slotId, physical: false, red);

        var theme = feed.ResolveTheme(new OverlayFeed.Request(ThemeId: null, SlotId: null, Physical: false));

        Assert.Same(red, theme);
    }

    [Fact]
    public void ResolveThemePhysicalRequestReturnsPhysicalChoice()
    {
        var red = Theme("dualsense-red");
        var white = Theme("dualsense-white");
        choices.Set(slotId, physical: false, red);
        choices.Set(slotId, physical: true, white);

        var theme = feed.ResolveTheme(new OverlayFeed.Request(ThemeId: null, slotId, Physical: true));

        Assert.Same(white, theme);
    }

    /// <summary>
    /// A URL can name a theme that is not installed any more. The overlay then follows the dashboard.
    /// </summary>
    [Fact]
    public void ResolveThemeUnknownExplicitThemeFallsBackToChoice()
    {
        var red = Theme("dualsense-red");
        choices.Set(slotId, physical: false, red);

        var theme = feed.ResolveTheme(new OverlayFeed.Request("not-installed", slotId, Physical: false));

        Assert.Same(red, theme);
    }

    [Fact]
    public void ResolveThemeChoiceRemovedReturnsStyleDefault()
    {
        choices.Set(slotId, physical: false, Theme("dualsense-red"));
        choices.Remove(slotId);

        var theme = feed.ResolveTheme(new OverlayFeed.Request(ThemeId: null, slotId, Physical: false));

        Assert.Null(theme);
    }
}
