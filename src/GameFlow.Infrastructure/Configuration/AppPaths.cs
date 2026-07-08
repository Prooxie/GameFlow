namespace GameFlow.Infrastructure.Configuration;

/// <summary>
/// Resolves on-disk locations for everything the app reads or writes outside
/// of the install directory: the per-user settings file, profile JSON, log
/// files, etc.
///
/// <para>
/// All public properties are evaluated on every access so that runtime
/// overrides applied through <see cref="AppPathOverrides"/> take effect
/// without restarting the process. Each call also creates the directory if
/// it does not already exist, so callers do not have to defensively
/// <c>Directory.CreateDirectory</c> before writing.
/// </para>
///
/// <para>
/// <b>Important:</b> <see cref="BaseDirectory"/> is intentionally not
/// overridable. The settings file lives there, so an overrideable base would
/// require the app to know where to read its own settings before it could
/// load them — a chicken-and-egg problem we avoid by fixing the base path.
/// </para>
/// </summary>
public static class AppPaths
{
    /// <summary>
    /// Per-user, per-machine root for everything this app writes:
    /// <c>%LocalAppData%/GAMEFLOW</c> on Windows and the platform
    /// equivalent elsewhere. Always exists by the time this returns.
    ///
    /// <para>
    /// This folder was named <c>AutofireNext</c> until the GameFlow rebrand
    /// completed; it stayed that way for a while afterward specifically to
    /// avoid orphaning existing installs' profiles and settings. It's now
    /// renamed on explicit request. To honour that without silently losing
    /// anyone's data, the first access in a process's lifetime performs a
    /// one-time, best-effort MIGRATION: if the legacy <c>AutofireNext</c>
    /// folder exists and this new folder is empty (or absent), every file
    /// is <b>copied</b> — never moved — into the new location. The legacy
    /// folder is left untouched either way, so it doubles as a backup; a
    /// migration failure (permissions, disk full, whatever) is swallowed
    /// and never blocks startup, worst case leaving the user on a fresh
    /// empty folder exactly as a new install would.
    /// </para>
    /// </summary>
    public static string BaseDirectory
    {
        get
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GAMEFLOW");
            MigrateFromLegacyFolderOnce(path);
            return Ensure(path);
        }
    }

    private static readonly object migrationGate = new();
    private static bool migrationAttempted;

    /// <summary>
    /// Copies every file/subfolder from the legacy <c>AutofireNext</c> data
    /// folder into <paramref name="newPath"/>, once per process, only when
    /// the legacy folder exists and the new one doesn't already have
    /// content (so a second migration attempt, or a user who already has a
    /// populated GAMEFLOW folder, is always a safe no-op). Copies, never
    /// deletes or moves — the legacy folder is left exactly as it was.
    /// </summary>
    private static void MigrateFromLegacyFolderOnce(string newPath)
    {
        if (migrationAttempted)
        {
            return;
        }
        lock (migrationGate)
        {
            if (migrationAttempted)
            {
                return;
            }
            migrationAttempted = true;

            try
            {
                var legacyPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AutofireNext");
                if (!Directory.Exists(legacyPath))
                {
                    return;
                }
                if (Directory.Exists(newPath) && Directory.EnumerateFileSystemEntries(newPath).Any())
                {
                    return;
                }
                CopyDirectoryRecursive(legacyPath, newPath);
            }
            catch
            {
                // Best-effort: migration failing must never prevent the
                // app from starting. The legacy folder is untouched, so
                // nothing is lost — worst case the user copies it by hand.
            }
        }
    }

    private static void CopyDirectoryRecursive(string sourceDir, string destDir)
    {
        _ = Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: false);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectoryRecursive(dir, Path.Combine(destDir, Path.GetFileName(dir)));
        }
    }

    /// <summary>
    /// Directory containing every saved profile JSON. Honours
    /// <see cref="AppPathOverrides.ProfilesDirectory"/> when set; otherwise
    /// falls back to <c>{BaseDirectory}/profiles</c>.
    /// </summary>
    public static string ProfilesDirectory =>
        Ensure(AppPathOverrides.ProfilesDirectory
               ?? Path.Combine(BaseDirectory, "profiles"));

    /// <summary>
    /// Directory containing rolling Serilog log files. Honours
    /// <see cref="AppPathOverrides.LogsDirectory"/> when set; otherwise
    /// falls back to <c>{BaseDirectory}/logs</c>.
    /// </summary>
    /// <remarks>
    /// Changing the override at runtime does NOT relocate the open Serilog
    /// file sink, which captures its target path once at host build. New
    /// log files appear under the override on the next process restart.
    /// Diagnostics and "open logs folder" actions in the UI, however, will
    /// see the override immediately because they call this property each
    /// time.
    /// </remarks>
    public static string LogsDirectory =>
        Ensure(AppPathOverrides.LogsDirectory
               ?? Path.Combine(BaseDirectory, "logs"));

    /// <summary>
    /// Directory the user can drop controller-overlay PNGs into so the
    /// dashboard renders them in place of the programmatic art. Falls
    /// back to <c>{BaseDirectory}/controller-overlays</c> when no
    /// override is set.
    ///
    /// <para>
    /// NOTE: this is the legacy PNG-overlay path. The theme system
    /// (ThemeRegistry + ThemeSurface) supersedes it, so for most users this
    /// folder stays empty. We therefore return the path WITHOUT creating
    /// the folder — there's no point littering %LocalAppData% with an empty
    /// directory on every launch. The folder is created on demand only if
    /// something actually writes an overlay (see EnsureOverlaysDirectory).
    /// </para>
    /// </summary>
    public static string ControllerOverlaysDirectory =>
        AppPathOverrides.ControllerOverlaysDirectory
        ?? Path.Combine(BaseDirectory, "controller-overlays");

    /// <summary>
    /// Same path as <see cref="ControllerOverlaysDirectory"/> but creates
    /// the folder. Call this only when about to write an overlay file.
    /// </summary>
    public static string EnsureOverlaysDirectory() => Ensure(ControllerOverlaysDirectory);

    /// <summary>
    /// Directory containing every installed VSCView-compatible controller
    /// theme. Each theme lives in its own subfolder
    /// (<c>themes/&lt;theme-id&gt;/theme.json</c>) alongside the PNG
    /// assets it references. Honours
    /// <see cref="AppPathOverrides.ThemesDirectory"/> when set; otherwise
    /// falls back to <c>{BaseDirectory}/themes</c>.
    ///
    /// <para>
    /// This is the folder users point a downloaded VSCView theme archive
    /// at. The same folder is also where the bundled
    /// "Xbox Series X — Default" theme is unpacked on first run so the
    /// app ships with at least one fully-functional controller surface
    /// out of the box.
    /// </para>
    /// </summary>
    public static string ThemesDirectory =>
        Ensure(AppPathOverrides.ThemesDirectory
               ?? Path.Combine(BaseDirectory, "themes"));

    /// <summary>
    /// Absolute path of the user-settings JSON file. Always inside
    /// <see cref="BaseDirectory"/> so it can be located before any user
    /// configuration has been loaded.
    /// </summary>
    public static string SettingsFile => Path.Combine(BaseDirectory, "settings.json");

    /// <summary>
    /// Absolute path of the JSON file storing per-device output templates
    /// (virtual-controller kind, lighting, rumble, FFB, adaptive triggers)
    /// keyed by device id. Inside <see cref="BaseDirectory"/>.
    /// </summary>
    public static string DeviceTemplatesFile => Path.Combine(BaseDirectory, "device-templates.json");

    /// <summary>Per-device button remaps (canonical button → raw index).</summary>
    public static string ButtonMapsFile => Path.Combine(BaseDirectory, "device-button-maps.json");

    /// <summary>
    /// Absolute path of the JSON file storing controller-slot definitions
    /// (each slot = assigned input device(s) + an output template driving
    /// one virtual controller). Inside <see cref="BaseDirectory"/>.
    /// </summary>
    public static string SlotsFile => Path.Combine(BaseDirectory, "controller-slots.json");

    /// <summary>
    /// Returns the absolute path of the JSON file backing a given profile id.
    /// Does not check whether the file exists.
    /// </summary>
    /// <param name="profileId">The profile id, e.g. <c>"speedrunner-default"</c>.</param>
    public static string GetProfileFile(string profileId)
    {
        return Path.Combine(ProfilesDirectory, $"{profileId}.json");
    }

    /// <summary>
    /// Creates the directory if it does not already exist and returns the
    /// path. Wrapped here so callers don't have to remember the discard.
    /// </summary>
    private static string Ensure(string path)
    {
        _ = Directory.CreateDirectory(path);
        return path;
    }
}
