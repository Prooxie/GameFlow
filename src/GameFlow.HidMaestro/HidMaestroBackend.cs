using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using Microsoft.Extensions.Logging;

namespace GameFlow.HidMaestro;

/// <summary>
/// The Windows backend. It finds and loads the drop-in SDK on first use, and
/// then gives every call to one <see cref="HidMaestroSession"/>.
/// </summary>
/// <remarks>
/// <para>
/// No member of this class names an SDK type. The runtime resolves the types
/// a method names when it compiles that method, and the SDK may not exist
/// until <see cref="HidMaestroSdkLocator.Load"/> succeeds. So the session is
/// created in a method that is not inlined, after the load.
/// </para>
/// <para>
/// One lock guards the search, the session start, the catalog and pad
/// creation. The runtime tick loop, the catalog service and the requirement
/// check all call in, from different threads.
/// </para>
/// <para>
/// While the SDK is not loaded, a missing or unsupported file is searched for
/// again at most once per <see cref="MissingSdkReprobeInterval"/>, so a
/// dropped-in DLL starts to work without a restart. A failure after the SDK
/// is found stays for the life of the process: a failing context must not be
/// created again every few seconds.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class HidMaestroBackend : IHidMaestroBackend
{
    /// <summary>The shortest time between two searches while the SDK is not loaded.</summary>
    public static readonly TimeSpan MissingSdkReprobeInterval = TimeSpan.FromSeconds(10);

    // DefaultRequirementChecker shows the same text. The dashboard matches "Administrator".
    private const string NotElevatedDescription =
        "HIDMaestro is present, but GameFlow is not running as Administrator. " +
        "HIDMaestro needs administrator rights to install its driver and create virtual " +
        "controllers — restart GameFlow as Administrator (right-click → Run as administrator).";

    private static readonly HidMaestroStatus NotWindowsStatus =
        new(HidMaestroState.NotWindows, "HIDMaestro is only available on Windows.");

    private readonly object gate = new();
    private readonly ILogger logger;
    private readonly Func<IReadOnlyList<string>> roots;
    private readonly Func<bool> isElevated;
    private readonly Func<DateTimeOffset> now;

    private HidMaestroSession? session;
    private HidMaestroStatus? settledStatus;
    private HidMaestroStatus? searchStatus;
    private DateTimeOffset lastSearchAt;

    /// <summary>
    /// Creates the backend that the app uses. It searches the real roots and
    /// reads the real elevation. Nothing is searched until the first call.
    /// </summary>
    public HidMaestroBackend(ILogger<HidMaestroBackend> logger)
        : this(
            logger,
            static () => HidMaestroSdkLocator.SearchRoots(
                Environment.GetEnvironmentVariable(HidMaestroSdkLocator.DirectoryOverrideVariable),
                AppContext.BaseDirectory),
            static () => Environment.IsPrivilegedProcess,
            static () => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>Creates a backend with injected roots, elevation and clock, for tests.</summary>
    internal HidMaestroBackend(
        ILogger logger, Func<IReadOnlyList<string>> roots, Func<bool> isElevated, Func<DateTimeOffset> now)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(isElevated);
        ArgumentNullException.ThrowIfNull(now);

        this.logger = logger;
        this.roots = roots;
        this.isElevated = isElevated;
        this.now = now;
    }

    /// <inheritdoc />
    public HidMaestroStatus GetStatus()
    {
        lock (gate)
        {
            return GetStatusLocked();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<HidMaestroCatalogProfile> GetCatalog()
    {
        lock (gate)
        {
            return GetStatusLocked().CanListCatalog && session is not null ? session.Catalog : [];
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The session already turns creation exceptions into a failure text.
    /// The catch here keeps the seam's no-throw contract for anything that
    /// escapes it. The text is built here, not with
    /// <see cref="HidMaestroSession.DescribeFailure"/>: its type tests would
    /// put SDK types into this method.
    /// </remarks>
    public bool TryCreatePad(HidMaestroPadRequest request, [NotNullWhen(true)] out IHidMaestroPad? pad, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            var status = GetStatusLocked();
            if (!status.CanCreatePads || session is null)
            {
                pad = null;
                failure = status.Description;
                return false;
            }

            try
            {
                return session.TryCreatePad(request, out pad, out failure);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "HIDMaestro controller creation failed for profile {Profile}.", request.ProfileId);
                pad = null;
                failure = $"HIDMaestro controller creation failed: {exception.GetType().Name}: {exception.Message}";
                return false;
            }
        }
    }

    /// <summary>
    /// Gives the pads created so far their final Windows names. Does nothing
    /// before the SDK is loaded. Never throws.
    /// </summary>
    /// <remarks>
    /// The call runs outside the lock. It can wait up to 5 seconds for
    /// Windows, and the status and catalog callers must not wait with it.
    /// A failure only leaves the names that Windows chose, so it is logged
    /// at Debug.
    /// </remarks>
    public void FinalizeNames()
    {
        HidMaestroSession? current;
        lock (gate)
        {
            current = session;
        }

        try
        {
            current?.FinalizeNames();
        }
        catch (Exception exception)
        {
            logger.LogDebug(
                exception, "HIDMaestro: FinalizeNames() failed; virtual controller names may show the driver default.");
        }
    }

    /// <summary>Callers must hold <see cref="gate"/>.</summary>
    private HidMaestroStatus GetStatusLocked()
    {
        if (!OperatingSystem.IsWindows())
        {
            return NotWindowsStatus;
        }

        if (settledStatus is not null)
        {
            return settledStatus;
        }

        var at = now();
        if (searchStatus is not null && at - lastSearchAt < MissingSdkReprobeInterval)
        {
            return searchStatus;
        }

        lastSearchAt = at;
        return Search();
    }

    /// <summary>Searches the roots once. Callers must hold <see cref="gate"/>.</summary>
    private HidMaestroStatus Search()
    {
        SdkLookup lookup;
        try
        {
            lookup = HidMaestroSdkLocator.Find(roots());
        }
        catch (Exception exception)
        {
            var failed = Failed(exception);
            if (failed != searchStatus)
            {
                logger.LogWarning(exception, "HIDMaestro: the SDK search failed; it runs again later.");
            }

            return searchStatus = failed;
        }

        HidMaestroStatus status;
        switch (lookup.Result)
        {
            case SdkLookupResult.Missing:
                status = new HidMaestroStatus(
                    HidMaestroState.SdkMissing,
                    $"{HidMaestroSdkLocator.FileName} not found. Place the SDK assembly next to the executable " +
                    $"(or in a 'HIDMaestro' subfolder, or point {HidMaestroSdkLocator.DirectoryOverrideVariable} " +
                    "at its folder) to activate HIDMaestro output. Searched: " + string.Join("; ", lookup.SearchedRoots));
                if (status != searchStatus)
                {
                    // The output sink gives the user-facing warning for its slot.
                    logger.LogDebug("HIDMaestro: {Status}", status.Description);
                }

                return searchStatus = status;

            case SdkLookupResult.Unsupported:
                status = new HidMaestroStatus(
                    HidMaestroState.SdkVersionUnsupported,
                    $"{HidMaestroSdkLocator.FileName} {lookup.Version?.ToString() ?? "(unreadable)"} found at " +
                    $"{lookup.Path}, but GameFlow needs HIDMaestro 2.0.1 or a later 2.x release.");
                if (status != searchStatus)
                {
                    logger.LogWarning("HIDMaestro: {Status}", status.Description);
                }

                return searchStatus = status;

            default:
                return settledStatus = Start(lookup.Path!);
        }
    }

    /// <summary>
    /// Loads the SDK and starts the session. The result is final for the
    /// process. Callers must hold <see cref="gate"/>.
    /// </summary>
    private HidMaestroStatus Start(string path)
    {
        try
        {
            _ = HidMaestroSdkLocator.Load(path);
            session = CreateSession();
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception, "HIDMaestro: the SDK at {Path} failed to start. Fix the cause and restart GameFlow.", path);
            return Failed(exception);
        }

        // Elevation cannot change while the process runs, so it is read once.
        var elevated = isElevated();
        logger.LogInformation(
            "HIDMaestro {Version} loaded from {Path}: {Count} profiles, elevated={Elevated}.",
            session.SdkVersion, path, session.Catalog.Count, elevated);

        if (!elevated)
        {
            logger.LogWarning(
                "HIDMaestro: GameFlow is not running as Administrator. The catalog is listed, but no " +
                "virtual controller can be created until GameFlow restarts as Administrator.");
            return new HidMaestroStatus(HidMaestroState.NotElevated, NotElevatedDescription);
        }

        return new HidMaestroStatus(
            HidMaestroState.Ready, $"HIDMaestro {session.SdkVersion} ready ({session.Catalog.Count} profiles).");
    }

    /// <summary>
    /// The only place that creates the session. Not inlined, so the SDK types
    /// that the session names are resolved only after the load succeeded.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private HidMaestroSession CreateSession() => new(logger);

    private static HidMaestroStatus Failed(Exception exception) =>
        new(HidMaestroState.Failed, $"HIDMaestro failed to start: {exception.Message}");
}
