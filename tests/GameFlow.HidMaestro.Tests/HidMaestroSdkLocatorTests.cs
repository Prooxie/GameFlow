using Xunit;

namespace GameFlow.HidMaestro.Tests;

public sealed class HidMaestroSdkLocatorTests : IDisposable
{
    private readonly string temp = Path.Combine(Path.GetTempPath(), "gf-hm-" + Guid.NewGuid().ToString("N"));

    public HidMaestroSdkLocatorTests() => Directory.CreateDirectory(temp);

    public void Dispose()
    {
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private string MakeDirectory(string name)
    {
        var path = Path.Combine(temp, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private string CopySdkInto(string directory)
    {
        var target = Path.Combine(directory, HidMaestroSdkLocator.FileName);
        File.Copy(typeof(HIDMaestro.HMContext).Assembly.Location, target);
        return target;
    }

    [WindowsFact]
    public void SearchRoots_OverrideFirst_ThenBase_ThenHidMaestroSubfolder()
    {
        var overrideDirectory = MakeDirectory("override");
        var baseDirectory = MakeDirectory("base");

        var roots = HidMaestroSdkLocator.SearchRoots(overrideDirectory, baseDirectory);

        Assert.Equal([overrideDirectory, baseDirectory, Path.Combine(baseDirectory, "HIDMaestro")], roots);
    }

    [WindowsFact]
    public void SearchRoots_MissingOverride_IsSkipped()
    {
        var baseDirectory = MakeDirectory("base");

        var roots = HidMaestroSdkLocator.SearchRoots(Path.Combine(temp, "nope"), baseDirectory);

        Assert.Equal([baseDirectory, Path.Combine(baseDirectory, "HIDMaestro")], roots);
    }

    [WindowsTheory]
    [InlineData("2.0.1", true)]
    [InlineData("2.0.1.0", true)]
    [InlineData("2.9.0", true)]
    [InlineData("2.0.0", false)]
    [InlineData("1.6.1", false)]
    [InlineData("3.0.0", false)]
    public void IsSupported_ComparesMajorMinorBuild(string version, bool expected) =>
        Assert.Equal(expected, HidMaestroSdkLocator.IsSupported(Version.Parse(version)));

    [WindowsFact]
    public void IsSupported_TreatsUnsetBuildAsZero() =>
        Assert.False(HidMaestroSdkLocator.IsSupported(new Version(2, 0)));

    [WindowsFact]
    public void Find_ReturnsTheFirstRootThatHasTheSdk()
    {
        var a = MakeDirectory("a");
        var b = MakeDirectory("b");
        var copy = CopySdkInto(b);

        var lookup = HidMaestroSdkLocator.Find([a, b]);

        Assert.Equal(SdkLookupResult.Found, lookup.Result);
        Assert.Equal(copy, lookup.Path);
        Assert.Equal(new Version(2, 0, 1, 0), lookup.Version);
    }

    [WindowsFact]
    public void Find_NoRootHasTheSdk_IsMissingAndListsTheRoots()
    {
        var roots = new[] { MakeDirectory("a"), MakeDirectory("b") };

        var lookup = HidMaestroSdkLocator.Find(roots);

        Assert.Equal(SdkLookupResult.Missing, lookup.Result);
        Assert.Null(lookup.Path);
        Assert.Equal(roots, lookup.SearchedRoots);
    }

    [WindowsFact]
    public void Find_GarbageFileWithTheSdkName_IsUnsupported()
    {
        var a = MakeDirectory("a");
        File.WriteAllBytes(Path.Combine(a, HidMaestroSdkLocator.FileName), new byte[16]);

        var lookup = HidMaestroSdkLocator.Find([a]);

        Assert.Equal(SdkLookupResult.Unsupported, lookup.Result);
        Assert.Null(lookup.Version);
    }

    [WindowsFact]
    public void Find_ReadableSdkOutsideTheRange_IsUnsupportedWithItsVersion()
    {
        var a = MakeDirectory("a");
        var path = FakeSdkAssembly.Emit(a, new Version(1, 6, 1, 0));

        var lookup = HidMaestroSdkLocator.Find([a]);

        Assert.Equal(SdkLookupResult.Unsupported, lookup.Result);
        Assert.Equal(new Version(1, 6, 1, 0), lookup.Version);
        Assert.Equal(path, lookup.Path);
        Assert.Equal(a, Path.GetDirectoryName(lookup.Path));
    }

    [WindowsFact]
    public void Load_ReturnsTheAlreadyLoadedSdk()
    {
        var copy = CopySdkInto(MakeDirectory("a"));

        Assert.Same(typeof(HIDMaestro.HMContext).Assembly, HidMaestroSdkLocator.Load(copy));
        Assert.Same(typeof(HIDMaestro.HMContext).Assembly, HidMaestroSdkLocator.Load(copy));
    }
}
