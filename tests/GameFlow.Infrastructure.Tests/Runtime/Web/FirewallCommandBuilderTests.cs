using GameFlow.Infrastructure.Runtime.Web;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

/// <summary>Tests for <see cref="FirewallCommandBuilder"/>.</summary>
public sealed class FirewallCommandBuilderTests
{
    private const string UserSid = "S-1-5-21-1000";

    /// <summary>
    /// HTTP.sys owns the socket, so a program filter blocks all LAN traffic.
    /// </summary>
    [Fact]
    public void BuildAllowCommandAnyPortHasNoProgramFilter()
    {
        var command = FirewallCommandBuilder.BuildAllowCommand(8080, UserSid);

        Assert.DoesNotContain("program=", command, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildAllowCommandAnyPortOpensOnlyThatTcpPort()
    {
        var command = FirewallCommandBuilder.BuildAllowCommand(8080, UserSid);

        Assert.Contains("protocol=TCP localport=8080 ", command, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAllowCommandAnyPortCoversAllProfiles()
    {
        var command = FirewallCommandBuilder.BuildAllowCommand(8080, UserSid);

        Assert.Contains("profile=any", command, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAllowCommandAnyPortAllowsOnlyLanAddresses()
    {
        var command = FirewallCommandBuilder.BuildAllowCommand(8080, UserSid);

        Assert.Contains($"remoteip={FirewallCommandBuilder.LanRemoteAddresses} ", command, StringComparison.Ordinal);
        Assert.StartsWith("localsubnet,", FirewallCommandBuilder.LanRemoteAddresses, StringComparison.Ordinal);
        Assert.Contains("192.168.0.0/16", FirewallCommandBuilder.LanRemoteAddresses, StringComparison.Ordinal);
        Assert.Contains("10.0.0.0/8", FirewallCommandBuilder.LanRemoteAddresses, StringComparison.Ordinal);
        Assert.Contains("172.16.0.0/12", FirewallCommandBuilder.LanRemoteAddresses, StringComparison.Ordinal);
    }

    /// <summary>
    /// Delete before add, so a second run replaces the rule and does not add a copy.
    /// </summary>
    [Fact]
    public void BuildAllowCommandAnyPortDeletesOldRuleBeforeAdd()
    {
        var command = FirewallCommandBuilder.BuildAllowCommand(8080, UserSid);

        var deleteAt = command.IndexOf("firewall delete rule", StringComparison.Ordinal);
        var addAt = command.IndexOf("firewall add rule", StringComparison.Ordinal);
        Assert.InRange(deleteAt, 0, addAt - 1);
    }

    /// <summary>
    /// "Everyone" is a localized account name. A SID works on every Windows language.
    /// </summary>
    [Fact]
    public void BuildAllowCommandWithUserSidReservesUrlBySid()
    {
        var command = FirewallCommandBuilder.BuildAllowCommand(8080, UserSid);

        Assert.Contains($"http add urlacl url=http://+:8080/ sddl=\"D:(A;;GX;;;{UserSid})\"", command, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildAllowCommandWithoutUserSidSkipsUrlReservation()
    {
        var command = FirewallCommandBuilder.BuildAllowCommand(8080, userSid: null);

        Assert.DoesNotContain("urlacl", command, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void BuildAllowCommandInvalidPortThrowsArgumentOutOfRangeException(int port)
    {
        void Build() => FirewallCommandBuilder.BuildAllowCommand(port, UserSid);

        _ = Assert.Throws<ArgumentOutOfRangeException>(Build);
    }
}
