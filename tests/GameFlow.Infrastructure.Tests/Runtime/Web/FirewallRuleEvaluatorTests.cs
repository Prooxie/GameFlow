using GameFlow.Infrastructure.Runtime.Web;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

/// <summary>Tests for <see cref="FirewallRuleEvaluator"/>.</summary>
public sealed class FirewallRuleEvaluatorTests
{
    private const int Port = 8080;
    private const int Domain = 0x1;
    private const int Private = 0x2;
    private const int Public = 0x4;
    private const int AllProfiles = int.MaxValue;

    private static FirewallRuleInfo Rule(
        string? localPorts = "8080",
        string? applicationName = null,
        int profiles = AllProfiles,
        int protocol = FirewallRuleEvaluator.ProtocolTcp) =>
        new(WindowsFirewallAccess.RuleName, IsInbound: true, IsEnabled: true, IsAllow: true,
            protocol, localPorts, applicationName, profiles);

    [Fact]
    public void EvaluateNoRuleReturnsMissing()
    {
        FirewallRuleInfo[] rules = [];

        var state = FirewallRuleEvaluator.Evaluate(rules, Private, Port);

        Assert.Equal(FirewallRuleState.Missing, state);
    }

    [Fact]
    public void EvaluatePortRuleWithoutProgramReturnsActive()
    {
        FirewallRuleInfo[] rules = [Rule()];

        var state = FirewallRuleEvaluator.Evaluate(rules, Public | Private, Port);

        Assert.Equal(FirewallRuleState.Active, state);
    }

    /// <summary>
    /// HttpListener connections arrive through HTTP.sys in the System process.
    /// A rule for GameFlow.App.exe never matches them.
    /// </summary>
    [Fact]
    public void EvaluateProgramScopedRuleReturnsStale()
    {
        FirewallRuleInfo[] rules = [Rule(applicationName: @"C:\GameFlow\GameFlow.App.exe")];

        var state = FirewallRuleEvaluator.Evaluate(rules, Private, Port);

        Assert.Equal(FirewallRuleState.Stale, state);
    }

    [Fact]
    public void EvaluateSystemProgramRuleReturnsActive()
    {
        FirewallRuleInfo[] rules = [Rule(applicationName: "System")];

        var state = FirewallRuleEvaluator.Evaluate(rules, Private, Port);

        Assert.Equal(FirewallRuleState.Active, state);
    }

    [Theory]
    [InlineData("9090")]
    [InlineData("80,443")]
    [InlineData("9000-9100")]
    public void EvaluateRuleForOtherPortReturnsStale(string localPorts)
    {
        FirewallRuleInfo[] rules = [Rule(localPorts)];

        var state = FirewallRuleEvaluator.Evaluate(rules, Private, Port);

        Assert.Equal(FirewallRuleState.Stale, state);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("80,8080")]
    [InlineData("8000-8100")]
    [InlineData(" 80 , 8080 ")]
    public void EvaluateRuleThatIncludesPortReturnsActive(string localPorts)
    {
        FirewallRuleInfo[] rules = [Rule(localPorts)];

        var state = FirewallRuleEvaluator.Evaluate(rules, Private, Port);

        Assert.Equal(FirewallRuleState.Active, state);
    }

    /// <summary>
    /// The active profiles are a set. One adapter on Private and one on Public
    /// need a rule that covers both.
    /// </summary>
    [Fact]
    public void EvaluateRuleThatMissesOneActiveProfileReturnsStale()
    {
        FirewallRuleInfo[] rules = [Rule(profiles: Domain | Private)];

        var state = FirewallRuleEvaluator.Evaluate(rules, Private | Public, Port);

        Assert.Equal(FirewallRuleState.Stale, state);
    }

    [Fact]
    public void EvaluateUdpRuleReturnsStale()
    {
        FirewallRuleInfo[] rules = [Rule(protocol: 17)];

        var state = FirewallRuleEvaluator.Evaluate(rules, Private, Port);

        Assert.Equal(FirewallRuleState.Stale, state);
    }

    [Fact]
    public void EvaluateAnyProtocolRuleReturnsActive()
    {
        FirewallRuleInfo[] rules = [Rule(protocol: FirewallRuleEvaluator.ProtocolAny)];

        var state = FirewallRuleEvaluator.Evaluate(rules, Private, Port);

        Assert.Equal(FirewallRuleState.Active, state);
    }

    [Fact]
    public void EvaluateDisabledOrBlockingRuleReturnsMissing()
    {
        FirewallRuleInfo[] rules =
        [
            Rule() with { IsEnabled = false },
            Rule() with { IsAllow = false },
            Rule() with { IsInbound = false },
        ];

        var state = FirewallRuleEvaluator.Evaluate(rules, Private, Port);

        Assert.Equal(FirewallRuleState.Missing, state);
    }

    [Fact]
    public void EvaluateRuleWithOtherNameReturnsMissing()
    {
        FirewallRuleInfo[] rules = [Rule() with { Name = "Some other app" }];

        var state = FirewallRuleEvaluator.Evaluate(rules, Private, Port);

        Assert.Equal(FirewallRuleState.Missing, state);
    }

    [Fact]
    public void EvaluateStaleAndValidRuleReturnsActive()
    {
        FirewallRuleInfo[] rules = [Rule(applicationName: @"C:\old\GameFlow.App.exe"), Rule()];

        var state = FirewallRuleEvaluator.Evaluate(rules, Private, Port);

        Assert.Equal(FirewallRuleState.Active, state);
    }

    [Theory]
    [InlineData(FirewallRuleState.Missing, true, true)]
    [InlineData(FirewallRuleState.Stale, true, true)]
    [InlineData(FirewallRuleState.Unknown, true, true)]
    [InlineData(FirewallRuleState.Active, true, false)]
    [InlineData(FirewallRuleState.Missing, false, false)]
    [InlineData(FirewallRuleState.Stale, false, false)]
    public void ShouldAddRuleStateAndElevationReturnsExpected(FirewallRuleState state, bool isElevated, bool expected)
    {
        var result = FirewallRuleEvaluator.ShouldAddRule(state, isElevated);

        Assert.Equal(expected, result);
    }
}
