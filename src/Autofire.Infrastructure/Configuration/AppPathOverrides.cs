namespace Autofire.Infrastructure.Configuration;

/// <summary>
/// Process-wide, thread-safe holder of user-supplied directory overrides.
///
/// <para>
/// The <see cref="AppPaths"/> static class reads these properties on every access
/// so that, once <see cref="UserSettingsService"/> has loaded the persisted
/// settings, every subsequent call (e.g. <c>AppPaths.ProfilesDirectory</c>) sees
/// the user's chosen location instead of the built-in default. Overrides that
/// stay <see langword="null"/> fall through to the OS-default location under
/// <c>%LocalAppData%/AutofireNext</c> (or the platform equivalent).
/// </para>
///
/// <para>
/// Notes on lifecycle:
/// <list type="bullet">
///   <item>
///     <description>
///       The <c>BaseDirectory</c> itself is intentionally NOT overridable — the
///       settings file lives there, so changing it would create a chicken-and-egg
///       problem on next launch.
///     </description>
///   </item>
///   <item>
///     <description>
///       The <c>LogsDirectory</c> override is honoured for any sink that reads
///       <see cref="AppPaths.LogsDirectory"/> at write-time. Serilog's static
///       file sink, however, captures the path once at host build, so a logs
///       directory change only takes effect on the next process restart. The
///       settings UI should communicate this.
///     </description>
///   </item>
///   <item>
///     <description>
///       The <c>ProfilesDirectory</c> override takes effect immediately for the
///       <see cref="Autofire.Infrastructure.Profiles.JsonProfileRepository"/>,
///       which is why <see cref="UserSettingsService.InitializeAsync"/> must run
///       before <c>ProfileSession.EnsureInitializedAsync</c>.
///     </description>
///   </item>
///   <item>
///     <description>
///       The <c>ThemesDirectory</c> override takes effect on the next
///       <c>ThemeRegistry.Refresh()</c> call. Settings UI changes should
///       trigger a refresh so freshly-installed themes appear without a
///       restart.
///     </description>
///   </item>
/// </list>
/// </para>
/// </summary>
public static class AppPathOverrides
{
    private static readonly object SyncRoot = new();

    private static string? profilesDirectory;
    private static string? logsDirectory;
    private static string? controllerOverlaysDirectory;
    private static string? themesDirectory;

    /// <summary>
    /// Optional override for <see cref="AppPaths.ProfilesDirectory"/>.
    /// Set to <see langword="null"/> or whitespace to fall back to the default.
    /// </summary>
    public static string? ProfilesDirectory
    {
        get
        {
            lock (SyncRoot)
            {
                return profilesDirectory;
            }
        }
        set
        {
            var normalised = NormaliseOrNull(value);
            lock (SyncRoot)
            {
                profilesDirectory = normalised;
            }
        }
    }

    /// <summary>
    /// Optional override for <see cref="AppPaths.LogsDirectory"/>.
    /// Set to <see langword="null"/> or whitespace to fall back to the default.
    /// </summary>
    public static string? LogsDirectory
    {
        get
        {
            lock (SyncRoot)
            {
                return logsDirectory;
            }
        }
        set
        {
            var normalised = NormaliseOrNull(value);
            lock (SyncRoot)
            {
                logsDirectory = normalised;
            }
        }
    }

    /// <summary>
    /// Optional override for <see cref="AppPaths.ControllerOverlaysDirectory"/>.
    /// Set to <see langword="null"/> or whitespace to fall back to the
    /// default <c>{BaseDirectory}/controller-overlays</c>.
    /// </summary>
    public static string? ControllerOverlaysDirectory
    {
        get
        {
            lock (SyncRoot)
            {
                return controllerOverlaysDirectory;
            }
        }
        set
        {
            var normalised = NormaliseOrNull(value);
            lock (SyncRoot)
            {
                controllerOverlaysDirectory = normalised;
            }
        }
    }

    /// <summary>
    /// Optional override for <see cref="AppPaths.ThemesDirectory"/>. The
    /// override is honoured by every <c>ThemeRegistry.Refresh()</c> call,
    /// so a settings UI change followed by a refresh picks up themes
    /// from the new location without a restart.
    /// </summary>
    public static string? ThemesDirectory
    {
        get
        {
            lock (SyncRoot)
            {
                return themesDirectory;
            }
        }
        set
        {
            var normalised = NormaliseOrNull(value);
            lock (SyncRoot)
            {
                themesDirectory = normalised;
            }
        }
    }

    /// <summary>
    /// Resets every override to <see langword="null"/>. Primarily useful for
    /// tests and for the "Restore defaults" action on the settings screen.
    /// </summary>
    public static void Reset()
    {
        lock (SyncRoot)
        {
            profilesDirectory = null;
            logsDirectory = null;
            controllerOverlaysDirectory = null;
            themesDirectory = null;
        }
    }

    /// <summary>
    /// Trims whitespace and rejects empty strings so that callers do not have
    /// to remember to do it themselves.
    /// </summary>
    private static string? NormaliseOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var trimmed = raw.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
