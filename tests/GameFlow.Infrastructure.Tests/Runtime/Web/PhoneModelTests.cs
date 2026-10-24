using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Localization;
using GameFlow.Infrastructure.Runtime.Web;
using GameFlow.Infrastructure.Runtime.Web.Overlay;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

public sealed class PhoneModelTests
{
    [Theory]
    [InlineData("xbox", WebPadLayout.Xbox, ControllerVisualStyle.Xbox360)]
    [InlineData("xbox360", WebPadLayout.Xbox, ControllerVisualStyle.Xbox360)]
    [InlineData("xboxone", WebPadLayout.XboxOne, ControllerVisualStyle.XboxOne)]
    [InlineData("xboxseries", WebPadLayout.XboxSeries, ControllerVisualStyle.XboxSeries)]
    [InlineData("ds4", WebPadLayout.DualShock4, ControllerVisualStyle.PlayStation4)]
    [InlineData("dualsense", WebPadLayout.DualSense, ControllerVisualStyle.PlayStation5)]
    [InlineData("switch", WebPadLayout.Nintendo, ControllerVisualStyle.NintendoSwitch)]
    public void ModelSelectsItsDashboardStyleAndKeepsTheInputContract(string wire, WebPadLayout expected, ControllerVisualStyle style)
    {
        var snapshot = WebControllerProtocol.TryParseInput("{\"b\":1,\"layout\":\"" + wire + "\"}", 0, out var layout);
        Assert.NotNull(snapshot);
        Assert.True(snapshot.IsPressed(ButtonId.South));
        Assert.Equal(expected, layout);
        Assert.Equal(style, layout!.Value.ToVisualStyle());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BrowserTranslationsCannotCloseTheScriptElement(bool overlay)
    {
        var localizer = new UnsafeTextLocalizer();
        var html = overlay ? OverlayAssets.RenderOverlayPage(localizer) : WebControllerAssets.RenderControllerPage(localizer);
        Assert.Contains("<html lang=\"cs\">", html);
        Assert.DoesNotContain(UnsafeTextLocalizer.Payload, html);
        Assert.Contains("\\u003C/script\\u003E", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "</script>"));
    }

    private sealed class UnsafeTextLocalizer : ILocalizationService
    {
        public const string Payload = "</script><script>alert('translation')</script>";
        public event EventHandler? CultureChanged { add { } remove { } }
        public IReadOnlyList<LanguageOption> SupportedLanguages => [];
        public string CurrentCulture => "cs";
        public string this[string key] => Payload;
        public string Translate(string key) => Payload;
        public void SetCulture(string cultureCode) { }
    }
}
