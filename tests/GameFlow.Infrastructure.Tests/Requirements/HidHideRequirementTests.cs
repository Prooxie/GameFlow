using GameFlow.Infrastructure.Requirements;
using GameFlow.Infrastructure.Requirements.HidHide;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using GameFlow.Infrastructure.Tests.Runtime.HidMaestro;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Requirements;

public sealed class HidHideRequirementTests
{
    private sealed class FakeHidHide : IHidHideService
    {
        public bool IsWanted { get; init; }
        public bool IsSupported => true;
        public bool Installed { get; init; }
        public bool IsInstalled() => Installed;
        public Task<HidHideInstallOutcome> InstallAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new HidHideInstallOutcome(HidHideInstallResult.Installed, "ok"));
    }

    private static async Task<RequirementStatus> CheckAsync(bool wanted, bool installed)
    {
        var backend = new FakeHidMaestroBackend { Status = new(HidMaestroState.Ready, "ready") };
        var checker = new DefaultRequirementChecker(
            NullLogger<DefaultRequirementChecker>.Instance, backend, new FakeHidHide { IsWanted = wanted, Installed = installed });

        var results = await checker.CheckAsync(TestContext.Current.CancellationToken);

        return Assert.Single(results, r => r.Id == "hidhide");
    }

    [Fact]
    public async Task NotWanted_IsNotApplicable()
    {
        var status = await CheckAsync(wanted: false, installed: false);

        Assert.False(status.IsApplicable);
        Assert.True(status.IsSatisfied);
    }

    [Fact]
    public async Task WantedAndMissing_OffersAutomaticInstall()
    {
        var status = await CheckAsync(wanted: true, installed: false);

        Assert.True(status.IsApplicable);
        Assert.False(status.IsSatisfied);
        Assert.True(status.CanAutoInstall);
    }

    [Fact]
    public async Task WantedAndInstalled_IsSatisfied()
    {
        var status = await CheckAsync(wanted: true, installed: true);

        Assert.True(status.IsApplicable);
        Assert.True(status.IsSatisfied);
    }

    [Theory]
    [InlineData(0, true, HidHideInstallResult.Installed)]
    [InlineData(0, false, HidHideInstallResult.Failed)]
    [InlineData(3010, true, HidHideInstallResult.RebootRequired)]
    [InlineData(1641, false, HidHideInstallResult.RebootRequired)]
    [InlineData(1602, false, HidHideInstallResult.Cancelled)]
    [InlineData(1223, false, HidHideInstallResult.Cancelled)]
    [InlineData(1603, false, HidHideInstallResult.Failed)]
    public void InstallerExitCodes_MapToResults(int exitCode, bool serviceInstalled, HidHideInstallResult expected) =>
        Assert.Equal(expected, HidHideService.MapInstallerExitCode(exitCode, serviceInstalled));

    [Theory]
    [InlineData("Nefarius Software Solutions e.U.", true)]
    [InlineData("nefarius software solutions", true)]
    [InlineData("Someone Else Ltd", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void OnlyNefariusSignedInstallersAreAccepted(string? signer, bool expected)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Equal(expected, AuthenticodeVerifier.IsExpectedPublisher(signer, HidHideService.ExpectedPublisher));
    }

    [Fact]
    public void InstallerUrlIsThePinnedOfficialHttpsAsset()
    {
        Assert.Equal(Uri.UriSchemeHttps, HidHideService.InstallerUrl.Scheme);
        Assert.Equal("github.com", HidHideService.InstallerUrl.Host);
        Assert.StartsWith("/nefarius/HidHide/releases/download/", HidHideService.InstallerUrl.AbsolutePath, StringComparison.Ordinal);
        Assert.EndsWith(HidHideService.InstallerFileName, HidHideService.InstallerUrl.AbsolutePath, StringComparison.Ordinal);
    }
}
