using GameFlow.Infrastructure.Theming;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Theming;

public sealed class ThemeBootstrapTests
{
    [Fact]
    public void SameLengthUpdatesReachUsersAndCustomArtworkIsBackedUp()
    {
        var root = Path.Combine(Path.GetTempPath(), "gameflow-theme-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "bundle", "test");
        var user = Path.Combine(root, "user");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "art.txt"), "before");
        Assert.Equal(1, ThemeBootstrap.EnsureBundledThemesInstalled(Path.Combine(root, "bundle"), user));
        Assert.Equal(0, ThemeBootstrap.EnsureBundledThemesInstalled(Path.Combine(root, "bundle"), user));
        File.WriteAllText(Path.Combine(user, "test", "custom.txt"), "my artwork");
        File.WriteAllText(Path.Combine(source, "art.txt"), "after!");
        Assert.Equal(1, ThemeBootstrap.EnsureBundledThemesInstalled(Path.Combine(root, "bundle"), user));
        Assert.Equal("after!", File.ReadAllText(Path.Combine(user, "test", "art.txt")));
        var backup = Assert.Single(Directory.GetDirectories(Path.Combine(root, "theme-backups")));
        Assert.Equal("my artwork", File.ReadAllText(Path.Combine(backup, "custom.txt")));
        Assert.Equal("before", File.ReadAllText(Path.Combine(backup, "art.txt")));
    }
}
