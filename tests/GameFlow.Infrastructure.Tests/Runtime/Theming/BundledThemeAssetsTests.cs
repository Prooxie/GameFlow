using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Runtime.Web.Overlay;
using GameFlow.Infrastructure.Tests.Theming;
using GameFlow.Infrastructure.Theming;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Theming;

/// <summary>Checks the artwork used by every bundled theme.</summary>
[Collection(ThemeFileCacheCollection.Name)]
public sealed class BundledThemeAssetsTests
{
    /// <summary>Gets every bundled theme manifest.</summary>
    public static TheoryData<string> Manifests()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.GetFiles(ThemesDirectory, "theme.json", SearchOption.AllDirectories))
        {
            data.Add(Path.GetRelativePath(ThemesDirectory, path));
        }

        return data;
    }

    /// <summary>Checks each image reference through the browser compiler.</summary>
    [Theory]
    [MemberData(nameof(Manifests))]
    public void BuildBundledThemeResolvesEveryImage(string manifest)
    {
        var path = Path.Combine(ThemesDirectory, manifest);
        var document = ThemeJsonLoader.LoadFromFile(path, ThemesDirectory);
        var theme = new InstalledTheme(manifest, document.Name, Path.GetDirectoryName(path)!,
            document, ControllerVisualStyle.Auto);

        var program = OverlayProgramBuilder.Build(theme);

        Assert.True(document.Width > 0);
        Assert.True(document.Height > 0);
        Assert.Empty(program.MissingImages);
        Assert.NotEmpty(program.ImagePaths);
        Assert.All(program.ImagePaths, image => Assert.True(File.Exists(image), image));
    }

    /// <summary>Gets the bundled theme directory in the source checkout.</summary>
    private static string ThemesDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "GameFlow.sln")))
                {
                    return Path.Combine(directory.FullName, "themes");
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("The GameFlow source checkout was not found.");
        }
    }
}
