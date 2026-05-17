using Autofire.Infrastructure.Configuration;
using Serilog;

namespace Autofire.Infrastructure.Theming;

/// <summary>
/// Copies the bundled "Xbox Series X — Default" theme (and any other
/// themes shipped under <c>{AppDirectory}/themes/</c>) into the user's
/// writable <see cref="AppPaths.ThemesDirectory"/> on first run.
///
/// <para>
/// The user-side themes folder is empty by default. A first-run copy
/// gives the dashboard something to render against straight after
/// install, while leaving the user free to delete / replace anything
/// later — we never overwrite an existing theme folder, so manual
/// edits are durable.
/// </para>
///
/// <para>
/// The class is also intentionally re-entrant: calling
/// <see cref="EnsureBundledThemesInstalled"/> on every startup is
/// cheap when everything is already in place (a single
/// <see cref="Directory.Exists(string)"/> per bundled theme). We trade
/// one no-op call per launch for the guarantee that a deleted user
/// theme always comes back the next time you start the app, mirroring
/// the way VSCView's own theme folder is repopulated from the build
/// output.
/// </para>
/// </summary>
public static class ThemeBootstrap
{
    /// <summary>
    /// Name of the subdirectory under the app's install directory that
    /// holds the bundled themes. Mirrors the user-side folder name so
    /// the copy is essentially a directory clone.
    /// </summary>
    public const string BundledThemesFolderName = "themes";

    /// <summary>
    /// Scans the install-side bundled-themes folder and copies any
    /// theme directory that doesn't already exist under the user-side
    /// <see cref="AppPaths.ThemesDirectory"/>. Returns the number of
    /// themes that were freshly installed so the caller can log a
    /// useful "X new themes available" message when desired.
    /// </summary>
    public static int EnsureBundledThemesInstalled()
    {
        // Locate the bundled themes folder relative to the running
        // assembly. AppContext.BaseDirectory points at the directory
        // containing the entry assembly's DLL/EXE, which is where MSBuild
        // copies the Content / AvaloniaResource tree at build time.
        var bundleRoot = Path.Combine(AppContext.BaseDirectory, BundledThemesFolderName);
        if (!Directory.Exists(bundleRoot))
        {
            Log.Debug(
                "No bundled-themes folder at {Bundle}; skipping first-run copy.",
                bundleRoot);
            return 0;
        }

        var userRoot = AppPaths.ThemesDirectory;
        var installedCount = 0;
        foreach (var src in Directory.EnumerateDirectories(bundleRoot))
        {
            var name = Path.GetFileName(src);
            if (string.IsNullOrWhiteSpace(name)) { continue; }

            var dst = Path.Combine(userRoot, name);
            if (Directory.Exists(dst))
            {
                // User already has this theme (or has explicitly
                // deleted only part of it). Leave it untouched so
                // local edits survive a re-install.
                continue;
            }

            try
            {
                CopyDirectoryRecursive(src, dst);
                installedCount++;
                Log.Information(
                    "Installed bundled theme '{Name}' into {Path}.",
                    name, dst);
            }
            catch (Exception ex)
            {
                Log.Warning(ex,
                    "Could not install bundled theme '{Name}' into {Path}.",
                    name, dst);
            }
        }

        return installedCount;
    }

    /// <summary>
    /// Pure helper that copies every file under <paramref name="source"/>
    /// to <paramref name="destination"/>, creating subdirectories as
    /// needed. Doesn't follow symlinks (the bundled themes don't use
    /// them) and skips zero-byte sentinel files like <c>.gitkeep</c> so
    /// the destination doesn't get cluttered with build artefacts.
    /// </summary>
    private static void CopyDirectoryRecursive(string source, string destination)
    {
        _ = Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source))
        {
            var name = Path.GetFileName(file);
            if (name.Equals(".gitkeep", StringComparison.Ordinal)) { continue; }

            File.Copy(file, Path.Combine(destination, name), overwrite: false);
        }

        foreach (var subdir in Directory.EnumerateDirectories(source))
        {
            var name = Path.GetFileName(subdir);
            CopyDirectoryRecursive(subdir, Path.Combine(destination, name));
        }
    }
}
