using GameFlow.Infrastructure.Configuration;
using Serilog;

namespace GameFlow.Infrastructure.Theming;

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
            var bundleStamp = ComputeBundleStamp(src);
            var stampPath = Path.Combine(dst, ".bundled.stamp");

            if (Directory.Exists(dst))
            {
                // A stamp marks the folder as a bundled copy that this
                // bootstrap owns. Same stamp → up to date, leave alone.
                // Different or MISSING stamp → the shipped content
                // changed since it was copied (missing covers copies made
                // by older bootstraps that never stamped) → refresh in
                // place. Without this, fixed bundled themes never reached
                // users who already had the earlier copies — they kept
                // rendering the old broken layouts forever. Users who
                // want to customise a default should copy it under a new
                // folder name; same-named folders are treated as ours.
                var existingStamp = File.Exists(stampPath) ? File.ReadAllText(stampPath).Trim() : null;
                if (string.Equals(existingStamp, bundleStamp, StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    Directory.Delete(dst, recursive: true);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex,
                        "Could not refresh bundled theme '{Name}' (delete failed); keeping the existing copy.",
                        name);
                    continue;
                }
                Log.Information(
                    "Bundled theme '{Name}' changed since it was installed — refreshing.",
                    name);
            }

            try
            {
                CopyDirectoryRecursive(src, dst);
                File.WriteAllText(stampPath, bundleStamp);
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
    /// Cheap content fingerprint of a bundled theme folder: relative
    /// path and length of every file, hashed. Regenerated art virtually
    /// always changes at least one file size, so a re-shipped theme
    /// yields a new stamp, while identical bits yield a stable one.
    /// </summary>
    private static string ComputeBundleStamp(string themeDir)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var file in Directory.EnumerateFiles(themeDir, "*", SearchOption.AllDirectories)
                     .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var info = new FileInfo(file);
            _ = builder.Append(Path.GetRelativePath(themeDir, file))
                       .Append('|').Append(info.Length).Append('\n');
        }
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes);
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
