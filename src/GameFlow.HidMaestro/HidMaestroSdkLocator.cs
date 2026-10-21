using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Versioning;

namespace GameFlow.HidMaestro;

/// <summary>Outcome of looking for the drop-in SDK assembly.</summary>
internal enum SdkLookupResult
{
    /// <summary>The file exists and its version is supported.</summary>
    Found,

    /// <summary>No search root holds the file.</summary>
    Missing,

    /// <summary>The file exists but is unreadable or has a version outside the supported range.</summary>
    Unsupported,
}

/// <summary>Result of <see cref="HidMaestroSdkLocator.Find"/>.</summary>
/// <param name="Result">The outcome.</param>
/// <param name="Path">The file path, or null when no file was found.</param>
/// <param name="Version">The assembly version, or null when it could not be read.</param>
/// <param name="SearchedRoots">The roots that were searched, for the user-facing message.</param>
internal sealed record SdkLookup(
    SdkLookupResult Result,
    string? Path,
    Version? Version,
    IReadOnlyList<string> SearchedRoots);

/// <summary>
/// Finds, version-checks and loads <c>HIDMaestro.Core.dll</c>. The user drops the
/// SDK in by hand. The app never ships it and its deps.json does not list it,
/// so the default load context cannot find it without help.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class HidMaestroSdkLocator
{
    /// <summary>Environment variable that points at a folder holding the SDK.</summary>
    public const string DirectoryOverrideVariable = "GAMEFLOW_HIDMAESTRO_DIR";

    /// <summary>File name of the SDK assembly.</summary>
    public const string FileName = "HIDMaestro.Core.dll";

    /// <summary>Lowest supported SDK version, inclusive.</summary>
    public static readonly Version MinimumVersion = new(2, 0, 1);

    /// <summary>First unsupported SDK version. A major bump may break the typed adapter.</summary>
    public static readonly Version ExclusiveMaximumVersion = new(3, 0, 0);

    private const string AssemblySimpleName = "HIDMaestro.Core";

    private static readonly object LoadLock = new();
    private static bool resolverInstalled;
    private static Assembly? resolvedAssembly;

    /// <summary>
    /// Lists the folders to search, in priority order: the override folder (when it
    /// exists), the app folder, then its <c>HIDMaestro</c> subfolder.
    /// </summary>
    public static IReadOnlyList<string> SearchRoots(string? overrideDirectory, string baseDirectory)
    {
        var roots = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(overrideDirectory) && Directory.Exists(overrideDirectory))
        {
            roots.Add(overrideDirectory);
        }

        roots.Add(baseDirectory);
        roots.Add(Path.Combine(baseDirectory, "HIDMaestro"));
        return roots;
    }

    /// <summary>
    /// Tells whether the adapter supports the SDK version. Only major.minor.build count,
    /// so 2.0.1.0 equals 2.0.1. Unset components count as 0.
    /// </summary>
    public static bool IsSupported(Version version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var normalized = Normalize(version);
        return normalized >= Normalize(MinimumVersion) && normalized < Normalize(ExclusiveMaximumVersion);
    }

    /// <summary>
    /// Uses the first root that holds the SDK file. Reads the version from the file
    /// metadata and never loads it, so a wrong version cannot run any SDK code.
    /// </summary>
    public static SdkLookup Find(IReadOnlyList<string> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        foreach (var root in roots)
        {
            var path = Path.Combine(root, FileName);
            if (!File.Exists(path))
            {
                continue;
            }

            Version? version;
            try
            {
                version = AssemblyName.GetAssemblyName(path).Version;
            }
            catch (Exception exception) when (exception is BadImageFormatException or FileLoadException)
            {
                return new SdkLookup(SdkLookupResult.Unsupported, path, null, roots);
            }

            var result = version is not null && IsSupported(version)
                ? SdkLookupResult.Found
                : SdkLookupResult.Unsupported;
            return new SdkLookup(result, path, version, roots);
        }

        return new SdkLookup(SdkLookupResult.Missing, null, null, roots);
    }

    /// <summary>
    /// Loads the SDK into the default context and makes its name resolvable there.
    /// Safe to call repeatedly and from several threads.
    /// </summary>
    public static Assembly Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        lock (LoadLock)
        {
            var assembly = AssemblyLoadContext.Default.Assemblies.FirstOrDefault(IsSdk)
                ?? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
            resolvedAssembly = assembly;

            if (!resolverInstalled)
            {
                AssemblyLoadContext.Default.Resolving += OnResolving;
                resolverInstalled = true;
            }

            return assembly;
        }
    }

    private static Assembly? OnResolving(AssemblyLoadContext context, AssemblyName name)
    {
        lock (LoadLock)
        {
            return string.Equals(name.Name, AssemblySimpleName, StringComparison.OrdinalIgnoreCase)
                ? resolvedAssembly
                : null;
        }
    }

    private static bool IsSdk(Assembly assembly) =>
        string.Equals(assembly.GetName().Name, AssemblySimpleName, StringComparison.OrdinalIgnoreCase);

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
