using System.Net;
using GameFlow.Infrastructure.Runtime.Web;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

public sealed class WebRequestPolicyTests
{
    [Theory]
    [InlineData("http://192.168.1.5:8080/ws", "http://192.168.1.5:8080", true)]
    [InlineData("http://localhost:8080/overlay/ws", "http://localhost:8080", true)]
    [InlineData("http://192.168.1.5:8080/ws", null, true)]
    [InlineData("http://192.168.1.5:8080/ws", "https://attacker.example", false)]
    [InlineData("http://192.168.1.5:8080/ws", "http://192.168.1.5:8000", false)]
    [InlineData("http://192.168.1.5:8080/ws", "null", false)]
    [InlineData("http://attacker.example:8080/ws", "http://attacker.example:8080", false)]
    [InlineData("http://192.168.1.5:8080/ws", "http://192.168.1.5:8080/path", false)]
    public void EnforcesLocalHostAndSameOrigin(string request, string? origin, bool expected)
    {
        Assert.Equal(expected, WebRequestPolicy.Allows(new Uri(request), origin, IPAddress.Parse("192.168.1.5")));
    }
}
