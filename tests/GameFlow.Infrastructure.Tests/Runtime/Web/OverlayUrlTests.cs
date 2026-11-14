using GameFlow.Infrastructure.Runtime.Web.Overlay;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

/// <summary>Tests for <see cref="OverlayUrl"/>.</summary>
public sealed class OverlayUrlTests
{
    private const string Token = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public void BuildSharedAddressWithTokenReturnsLinkForOtherDevices()
    {
        var url = OverlayUrl.Build("http://192.168.1.20:8080", "abc", null, false, accessToken: Token);

        Assert.Equal($"http://192.168.1.20:8080/overlay?token={Token}&slot=abc", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void BuildNoTokenReturnsLinkWithoutToken(string? accessToken)
    {
        var url = OverlayUrl.Build("http://localhost:8080", "abc", null, false, accessToken: accessToken);

        Assert.Equal("http://localhost:8080/overlay?slot=abc", url);
    }
}
