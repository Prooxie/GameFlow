using GameFlow.Infrastructure.Theming;
using GameFlow.Infrastructure.Theming.Flee;
using GameFlow.Infrastructure.Theming.Models;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Theming;

/// <summary>Tests for <see cref="ThemeTouchpadLocator"/>.</summary>
public sealed class ThemeTouchpadLocatorTests
{
    private static ThemeDocument Document(params ThemeNode[] children) =>
        new() { Width = 400, Height = 300, Children = children };

    private static ShowHideNode Touchpad(string input, ImageNode image) =>
        new() { Input = FleeParser.Parse(input), Children = [image] };

    [Fact]
    public void FindTouchpadClickImageReturnsImageRectangle()
    {
        var document = Document(Touchpad(
            "touch_center:click",
            new ImageNode { X = 100, Y = 50, Width = 80, Height = 40 }));

        var region = ThemeTouchpadLocator.Find(document);

        Assert.Equal(new ThemeRegion(100, 50, 80, 40), region);
    }

    [Fact]
    public void FindCentredImageReturnsRectangleAroundCentre()
    {
        var document = Document(Touchpad(
            "touch_center:click",
            new ImageNode { X = 100, Y = 50, Width = 80, Height = 40, Center = true }));

        var region = ThemeTouchpadLocator.Find(document);

        Assert.Equal(new ThemeRegion(60, 30, 80, 40), region);
    }

    /// <summary>
    /// The touchpad is often a child of the body image. The parent offset applies.
    /// </summary>
    [Fact]
    public void FindNestedTouchpadAddsParentOffsets()
    {
        var document = Document(new ImageNode
        {
            X = 200,
            Y = 150,
            Width = 400,
            Height = 300,
            Children = [Touchpad("touch_center:click", new ImageNode { X = -20, Y = -60, Width = 40, Height = 20 })]
        });

        var region = ThemeTouchpadLocator.Find(document);

        Assert.Equal(new ThemeRegion(180, 90, 40, 20), region);
    }

    [Fact]
    public void FindCompoundTouchpadExpressionUsesFirstVariable()
    {
        var document = Document(Touchpad(
            "touch_center:click OR touch_right:click",
            new ImageNode { X = 10, Y = 20, Width = 30, Height = 40 }));

        var region = ThemeTouchpadLocator.Find(document);

        Assert.Equal(new ThemeRegion(10, 20, 30, 40), region);
    }

    [Fact]
    public void FindThemeWithoutTouchpadReturnsNull()
    {
        var document = Document(Touchpad("quad_right:s", new ImageNode { Width = 30, Height = 40 }));

        var region = ThemeTouchpadLocator.Find(document);

        Assert.Null(region);
    }

    [Fact]
    public void FindTouchpadImageWithoutSizeReturnsNull()
    {
        var document = Document(Touchpad("touch_center:click", new ImageNode { X = 10, Y = 20 }));

        var region = ThemeTouchpadLocator.Find(document);

        Assert.Null(region);
    }
}
