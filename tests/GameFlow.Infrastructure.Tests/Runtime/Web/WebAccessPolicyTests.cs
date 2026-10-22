using GameFlow.Infrastructure.Runtime.Web;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

public sealed class WebAccessPolicyTests
{
    [Theory]
    [InlineData(true, false, null, true)]
    [InlineData(false, false, "valid", false)]
    [InlineData(false, true, null, false)]
    [InlineData(false, true, "invalid", false)]
    [InlineData(false, true, "valid", true)]
    public void RemoteClientsRequireBothConsentAndTheSecret(bool loopback, bool sharing, string? supplied, bool expected)
    {
        var secret = new string('a', 64);
        Assert.Equal(expected, WebAccessPolicy.IsAuthorized(loopback, sharing, secret, supplied == "valid" ? secret : supplied));
    }
}
