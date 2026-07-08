using GameFlow.Infrastructure.Profiles;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Configuration;

/// <summary>
/// Default <see cref="IUserSettingsService"/> implementation.
///
/// <para>
/// Persists through <see cref="IProfileRepository"/> (which already owns the
/// settings JSON read/write) so the file format stays in one place. Pushes
/// log-level changes through <see cref="ILogLevelSwitch"/> and pushes path
/// overrides into <see cref="AppPathOverrides"/>.
/// </para>
///
/// <para>
/// Thread-safe: read access to <see cref="Current"/> is lock-free thanks to
/// <c>volatile</c>; <see cref="ApplyAsync"/> and <see cref="InitializeAsync"/>
/// serialise on a single async semaphore.
/// </para>
/// </summary>
public sealed class UserSettingsService : IUserSettingsService
{
    private readonly IProfileRepository repository;
    private readonly ILogLevelSwitch logLevelSwitch;
    private readonly ILogger<UserSettingsService> logger;
    private readonly SemaphoreSlim gate = new(1, 1);

    private volatile AppSettings current = new();
    private bool initialized;

    /// <inheritdoc />
    public event EventHandler<UserSettingsChangedEventArgs>? Changed;

    /// <summary>
    /// Constructs the service. The <paramref name="logLevelSwitch"/> is the
    /// adapter the App layer wired into the Serilog pipeline at host build,
    /// so writes to it propagate to every active sink.
    /// </summary>
    public UserSettingsService(
        IProfileRepository repository,
        ILogLevelSwitch logLevelSwitch,
        ILogger<UserSettingsService> logger)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.logLevelSwitch = logLevelSwitch ?? throw new ArgumentNullException(nameof(logLevelSwitch));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public AppSettings Current => current;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (initialized)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized)
            {
                return;
            }

            AppSettings loaded;
            try
            {
                loaded = await repository.LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // A corrupt settings file should never stop the app from
                // starting — fall back to defaults and log the failure.
                logger.LogWarning(
                    exception,
                    "Could not read settings file at {Path}; falling back to defaults.",
                    AppPaths.SettingsFile);
                loaded = new AppSettings();
            }

            ApplySideEffects(loaded);
            current = loaded;
            initialized = true;

            logger.LogInformation(
                "User settings loaded. LogLevel={LogLevel}, ProfilesDir={ProfilesDir}, LogsDir={LogsDir}.",
                loaded.LogLevel,
                AppPaths.ProfilesDirectory,
                AppPaths.LogsDirectory);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ApplyAsync(AppSettings updated, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updated);

        AppSettings previous;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            previous = current;

            await repository.SaveSettingsAsync(updated, cancellationToken).ConfigureAwait(false);
            ApplySideEffects(updated);
            current = updated;

            logger.LogInformation(
                "User settings updated. LogLevel: {OldLogLevel}->{NewLogLevel}, " +
                "ProfilesDir: {OldProfilesDir}->{NewProfilesDir}, LogsDir: {OldLogsDir}->{NewLogsDir}.",
                previous.LogLevel,
                updated.LogLevel,
                previous.ProfilesDirectoryOverride ?? "(default)",
                updated.ProfilesDirectoryOverride ?? "(default)",
                previous.LogsDirectoryOverride ?? "(default)",
                updated.LogsDirectoryOverride ?? "(default)");
        }
        finally
        {
            _ = gate.Release();
        }

        // Raised outside the lock so a misbehaving handler cannot deadlock
        // a subsequent ApplyAsync call.
        try
        {
            Changed?.Invoke(this, new UserSettingsChangedEventArgs(previous, updated));
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "A UserSettingsService.Changed subscriber threw.");
        }
    }

    /// <summary>
    /// Pushes the parts of <paramref name="settings"/> that have observable
    /// side-effects (log level, path overrides) into the runtime so the
    /// rest of the app picks them up.
    /// </summary>
    private void ApplySideEffects(AppSettings settings)
    {
        logLevelSwitch.Set(settings.LogLevel);
        AppPathOverrides.ProfilesDirectory = settings.ProfilesDirectoryOverride;
        AppPathOverrides.LogsDirectory = settings.LogsDirectoryOverride;
    }
}
