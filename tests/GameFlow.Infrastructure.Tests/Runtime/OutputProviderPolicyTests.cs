using GameFlow.Infrastructure.Runtime;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

/// <summary>Checks output migration for Windows, native platforms, and unsupported operating systems.</summary>
public sealed class OutputProviderPolicyTests
{
    /// <summary>Both native platforms select real output for persisted provider identifiers.</summary>
    [Theory]
    [InlineData("preview", true, false)]
    [InlineData("hidmaestro", false, true)]
    [InlineData(null, true, false)]
    [InlineData("retired", false, true)]
    public void ResolveNativePlatformSelectsNativeOutput(string? requested, bool isLinux, bool isMacOS)
    {
        var actual = OutputProviderPolicy.Resolve(requested, isWindows: false, isLinux, isMacOS);

        Assert.Equal("native-gamepad", actual);
    }
    [Theory]
    [InlineData("hidmaestro")]
    [InlineData("HIDMaestro")]
    [InlineData("  hidmaestro  ")]
    [InlineData("preview")]
    [InlineData("vigem-ds5")]
    [InlineData("vigem-xbox360")]
    [InlineData("vjoy")]
    [InlineData("something-unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveWindowsSelectsHidMaestro(string? requested)
    {
        Assert.Equal(OutputProviderPolicy.HidMaestro, OutputProviderPolicy.Resolve(requested, isWindows: true));
    }

    [Theory]
    [InlineData("hidmaestro")]
    [InlineData("preview")]
    [InlineData("vigem-ds4")]
    [InlineData(null)]
    public void ResolveUnsupportedPlatformSelectsPreview(string? requested)
    {
        Assert.Equal(OutputProviderPolicy.Preview, OutputProviderPolicy.Resolve(requested, isWindows: false));
    }

    [Theory]
    [InlineData("preview", true)]
    [InlineData("vigem-ds5", true)]
    [InlineData("VIGEM-XBOX360", true)]
    [InlineData("unknown-backend", true)]
    [InlineData("hidmaestro", false)]
    [InlineData("HidMaestro", false)]
    public void RequiresMigrationMatchesWindowsResolution(string requested, bool expectsMigration)
    {
        Assert.Equal(expectsMigration, OutputProviderPolicy.RequiresMigration(requested, isWindows: true));
    }

    [Fact]
    public void RequiresMigrationEmptyIdentifierInheritsDefault()
    {
        Assert.False(OutputProviderPolicy.RequiresMigration(null, isWindows: true));
        Assert.False(OutputProviderPolicy.RequiresMigration("   ", isWindows: true));
        Assert.False(OutputProviderPolicy.RequiresMigration(string.Empty, isWindows: false));
    }

    [Theory]
    [InlineData("vigem-xbox360", true)]
    [InlineData("vigem-ds4", true)]
    [InlineData("vigem-ds5", true)]
    [InlineData("vigem-dualsense", true)]
    [InlineData("VJOY", true)]
    [InlineData("hidmaestro", false)]
    [InlineData("preview", false)]
    [InlineData("", false)]
    public void IsLegacyProviderIdRecognizesRetiredProviders(string id, bool isLegacy)
    {
        Assert.Equal(isLegacy, OutputProviderPolicy.IsLegacyProviderId(id));
    }

    [Fact]
    public void NormalizeTrimsAndLowercases()
    {
        Assert.Equal("hidmaestro", OutputProviderPolicy.Normalize("  HidMaestro "));
        Assert.Equal(string.Empty, OutputProviderPolicy.Normalize(null));
        Assert.Equal(string.Empty, OutputProviderPolicy.Normalize("   "));
    }
}
