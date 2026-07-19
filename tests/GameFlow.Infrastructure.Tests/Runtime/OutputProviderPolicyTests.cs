using GameFlow.Infrastructure.Runtime;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

/// <summary>
/// Contract of the single-output-backend rule: on Windows every provider
/// id — current, legacy, empty, or unknown — resolves to HIDMaestro; on
/// non-Windows platforms everything resolves to the preview sink. This
/// is the rule that closed the "input works but no virtual controller
/// ever appears" class of bug, where stale ids quietly landed on a
/// preview fallback.
/// </summary>
public sealed class OutputProviderPolicyTests
{
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
    public void On_windows_everything_resolves_to_hidmaestro(string? requested)
    {
        Assert.Equal(OutputProviderPolicy.HidMaestro, OutputProviderPolicy.Resolve(requested, isWindows: true));
    }

    [Theory]
    [InlineData("hidmaestro")]
    [InlineData("preview")]
    [InlineData("vigem-ds4")]
    [InlineData(null)]
    public void Off_windows_everything_resolves_to_preview(string? requested)
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
    public void Migration_flag_matches_windows_resolution(string requested, bool expectsMigration)
    {
        Assert.Equal(expectsMigration, OutputProviderPolicy.RequiresMigration(requested, isWindows: true));
    }

    [Fact]
    public void Empty_id_means_inherit_and_is_not_flagged_for_migration()
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
    public void Legacy_ids_are_recognised(string id, bool isLegacy)
    {
        Assert.Equal(isLegacy, OutputProviderPolicy.IsLegacyProviderId(id));
    }

    [Fact]
    public void Normalize_trims_and_lowercases()
    {
        Assert.Equal("hidmaestro", OutputProviderPolicy.Normalize("  HidMaestro "));
        Assert.Equal(string.Empty, OutputProviderPolicy.Normalize(null));
        Assert.Equal(string.Empty, OutputProviderPolicy.Normalize("   "));
    }
}
