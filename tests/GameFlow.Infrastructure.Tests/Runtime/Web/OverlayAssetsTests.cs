using GameFlow.Infrastructure.Runtime.Web.Overlay;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

/// <summary>
/// Tests for the page in <see cref="OverlayAssets"/>. The page has no JavaScript test runner,
/// so these tests check the page source for each contract.
/// </summary>
public sealed class OverlayAssetsTests
{
    [Fact]
    public void OverlayPageTouchRegionDrawsFingers()
    {
        var page = OverlayAssets.OverlayPage;

        Assert.Contains("data.touchRegion", page, StringComparison.Ordinal);
        Assert.Contains("data.touch", page, StringComparison.Ordinal);
        Assert.Contains("drawTouches(", page, StringComparison.Ordinal);
    }

    [Fact]
    public void OverlayPageDefaultBackgroundIsTransparent()
    {
        var page = OverlayAssets.OverlayPage;

        Assert.Contains("background:transparent", page, StringComparison.Ordinal);
    }
}
