using GameFlow.Infrastructure.Profiles;

namespace GameFlow.Infrastructure.Configuration;

/// <summary>
/// Live user-settings service.
///
/// <para>
/// Loads the persisted <see cref="AppSettings"/> JSON once at startup, exposes
/// it as a hot reference, and lets the rest of the app subscribe to changes
/// (so e.g. the shell can re-bind window size, the dashboard can re-pace
/// itself, etc.). The service also owns the side-effects of certain settings:
/// it pushes the chosen log level into Serilog and applies path overrides
/// to <see cref="AppPathOverrides"/>.
/// </para>
///
/// <para>
/// This is a singleton — there is only one settings file on disk and one
/// in-memory copy of it. It is also the only writer of that file:
/// <see cref="ProfileSession"/> writes the active profile and the culture
/// through <see cref="UpdateAsync"/> too. Two writers with two copies put
/// each other's old values back on every save.
/// </para>
/// </summary>
public interface IUserSettingsService
{
    /// <summary>
    /// The currently in-effect settings. Always non-null. Replaced by a new
    /// instance every time <see cref="UpdateAsync"/> succeeds, so callers
    /// should treat the returned reference as a snapshot.
    /// </summary>
    AppSettings Current { get; }

    /// <summary>
    /// Raised after <see cref="UpdateAsync"/> persists a new settings value.
    /// Fires on a thread-pool thread; UI subscribers must marshal to the
    /// dispatcher themselves.
    /// </summary>
    event EventHandler<UserSettingsChangedEventArgs>? Changed;

    /// <summary>
    /// Loads <see cref="AppPaths.SettingsFile"/> if it exists, applies
    /// path overrides and the log level switch from the loaded values, and
    /// becomes ready. Safe to call exactly once at startup; subsequent calls
    /// are no-ops.
    /// </summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies <paramref name="change"/> to the latest settings, persists the
    /// result, applies its side-effects (log-level switch, path overrides),
    /// updates <see cref="Current"/>, and raises <see cref="Changed"/>.
    /// </summary>
    /// <remarks>
    /// The change runs under the write lock, on the settings as they are at
    /// that moment. Change only the fields that you own, for example
    /// <c>settings =&gt; settings with { MotionServerEnabled = true }</c>.
    /// A change that returns an equal value writes nothing. Loads the file
    /// first when <see cref="InitializeAsync"/> did not run yet, so an early
    /// change cannot write defaults over the saved settings.
    /// </remarks>
    /// <param name="change">Makes the new settings from the current ones. Must not have side effects.</param>
    /// <param name="cancellationToken">Cancellation for the disk write.</param>
    Task UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken = default);
}

/// <summary>
/// Payload for <see cref="IUserSettingsService.Changed"/>. Carries both the
/// previous and the new <see cref="AppSettings"/> so subscribers can react
/// only to the fields that actually changed.
/// </summary>
/// <param name="Previous">The settings as they were before the apply.</param>
/// <param name="Current">The settings as they are after the apply.</param>
public sealed record UserSettingsChangedEventArgs(AppSettings Previous, AppSettings Current);
