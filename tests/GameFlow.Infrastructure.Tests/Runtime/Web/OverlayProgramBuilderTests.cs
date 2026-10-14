using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Runtime.Web.Overlay;
using GameFlow.Infrastructure.Theming;
using GameFlow.Infrastructure.Theming.Flee;
using GameFlow.Infrastructure.Theming.Models;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

/// <summary>Tests for <see cref="OverlayProgramBuilder"/>.</summary>
public sealed class OverlayProgramBuilderTests
{
    private static OverlayProgram Build(params ThemeNode[] children)
    {
        // The builder caches per theme id and folder. A unique folder gives each test its own entry.
        var theme = new InstalledTheme(
            "touch-test", "Touch test", "C:\\themes\\" + Guid.NewGuid().ToString("N"),
            new ThemeDocument { Width = 400, Height = 300, Children = children },
            ControllerVisualStyle.PlayStation4);

        return OverlayProgramBuilder.Build(theme);
    }

    private static ShowHideNode TouchpadClick() => new()
    {
        Input = FleeParser.Parse("touch_center:click"),
        Children = [new ImageNode { X = 100, Y = 50, Width = 80, Height = 40 }]
    };

    private static TrailPadNode TrailPad() => new()
    {
        Input = FleeParser.Parse("touch_center:0:touch"),
        InputX = FleeParser.Parse("touch_center:0:x * 100"),
        InputY = FleeParser.Parse("touch_center:0:y * 100")
    };

    /// <summary>
    /// Most themes have no trailpad node. The app then draws the fingers over the touchpad rectangle.
    /// </summary>
    [Fact]
    public void BuildThemeWithoutTrailPadReturnsTouchRegion()
    {
        var program = Build(TouchpadClick());

        Assert.Equal(new ThemeRegion(100, 50, 80, 40), program.TouchRegion);
    }

    /// <summary>
    /// A trailpad theme draws its own finger art. A second finger layer would draw each finger twice.
    /// </summary>
    [Fact]
    public void BuildThemeWithTrailPadReturnsNoTouchRegion()
    {
        var program = Build(TouchpadClick(), TrailPad());

        Assert.Null(program.TouchRegion);
    }

    [Fact]
    public void BuildThemeWithoutTouchpadReturnsNoTouchRegion()
    {
        var program = Build(new ImageNode { Width = 10, Height = 10 });

        Assert.Null(program.TouchRegion);
    }
}
