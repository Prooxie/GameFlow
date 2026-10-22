using System.Reflection;
using System.Reflection.Emit;

namespace GameFlow.HidMaestro.Tests;

/// <summary>
/// Writes a stand-in <c>HIDMaestro.Core.dll</c> with a chosen version and no
/// SDK types. The locator reads only the metadata, so the file is enough to
/// test the version check without a second real SDK.
/// </summary>
internal static class FakeSdkAssembly
{
    /// <summary>Saves the stand-in into <paramref name="directory"/> and returns its path.</summary>
    public static string Emit(string directory, Version version)
    {
        var name = new AssemblyName("HIDMaestro.Core") { Version = version };
        var builder = new PersistedAssemblyBuilder(name, typeof(object).Assembly);
        _ = builder.DefineDynamicModule(HidMaestroSdkLocator.FileName);

        var path = Path.Combine(directory, HidMaestroSdkLocator.FileName);
        builder.Save(path);
        return path;
    }
}
