using GameFlow.Infrastructure.Requirements;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using GameFlow.Infrastructure.Tests.Runtime.HidMaestro;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Requirements;

public sealed class HidMaestroRequirementTests
{
    private static async Task<RequirementStatus> CheckAsync(HidMaestroState state)
    {
        var backend = new FakeHidMaestroBackend { Status = new(state, "reason") };
        var checker = new DefaultRequirementChecker(NullLogger<DefaultRequirementChecker>.Instance, backend);

        var results = await checker.CheckAsync();

        return Assert.Single(results);
    }

    [Fact]
    public async Task NotWindows_IsNotApplicable()
    {
        var status = await CheckAsync(HidMaestroState.NotWindows);

        Assert.False(status.IsApplicable);
        Assert.Equal("hidmaestro", status.Id);
    }

    [Fact]
    public async Task Ready_IsSatisfied()
    {
        var status = await CheckAsync(HidMaestroState.Ready);

        Assert.True(status.IsApplicable);
        Assert.True(status.IsSatisfied);
    }

    [Fact]
    public async Task NotElevated_IsUnsatisfiedWithTheElevationDescription()
    {
        var status = await CheckAsync(HidMaestroState.NotElevated);

        Assert.True(status.IsApplicable);
        Assert.False(status.IsSatisfied);
        Assert.Contains("Administrator", status.Description);
        Assert.NotNull(status.InstallerUrl);
    }

    [Theory]
    [InlineData(HidMaestroState.SdkMissing)]
    [InlineData(HidMaestroState.SdkVersionUnsupported)]
    [InlineData(HidMaestroState.Failed)]
    public async Task OtherStates_AreUnsatisfiedWithTheBaseDescription(HidMaestroState state)
    {
        var status = await CheckAsync(state);

        Assert.True(status.IsApplicable);
        Assert.False(status.IsSatisfied);
        Assert.DoesNotContain("Administrator", status.Description);
        Assert.Contains("only output backend", status.Description);
        Assert.NotNull(status.InstallerUrl);
    }
}
