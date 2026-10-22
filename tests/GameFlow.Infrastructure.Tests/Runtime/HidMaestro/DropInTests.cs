using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.HidMaestro;

/// <summary>
/// Guards the App to adapter reference: the SDK must stay a drop-in.
/// </summary>
public sealed class DropInTests
{
    [Fact]
    public void HidMaestroCore_IsNotCopiedIntoTheOutput() =>
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "HIDMaestro.Core.dll")));

    [Fact]
    public void HidMaestroCore_IsNotInTheDependencyManifest() =>
        Assert.DoesNotContain("HIDMaestro.Core",
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "GameFlow.Infrastructure.Tests.deps.json")));
}
