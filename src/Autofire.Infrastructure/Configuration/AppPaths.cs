namespace Autofire.Infrastructure.Configuration;

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
    /// Per-user, per-machine root for everything Autofire writes:
    /// <c>%LocalAppData%/AutofireNext</c> on Windows and the platform
    /// equivalent elsewhere. Always exists by the time this returns.
    /// </summary>
    public static string BaseDirectory => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutofireNext"));

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
    /// override is set. The folder is created on first access so users
    /// see a real destination when they open the parent folder.
    ///
    /// <para>
    /// Expected file names (case-insensitive, .png or .svg):
    /// <c>xbox</c>, <c>playstation4</c>, <c>playstation5</c>,
    /// <c>playstation3</c>, <c>switch</c>, <c>steamdeck</c>,
    /// <c>steamcontroller</c>, <c>arcade</c>. The full canonical list
    /// lives in <c>ControllerOverlayAssetLoader</c>.
    /// </para>
    /// </summary>
    public static string ControllerOverlaysDirectory =>
        Ensure(AppPathOverrides.ControllerOverlaysDirectory
               ?? Path.Combine(BaseDirectory, "controller-overlays"));

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
