using GameFlow.Infrastructure.Runtime.HidMaestro;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Requirements;

/// <summary>
/// Default <see cref="IRequirementChecker"/>. Knows about a single
/// requirement: HIDMaestro, the sole output backend (ViGEm Bus was
/// retired as a dependency). Windows-only, like the requirement itself.
///
/// <para>
/// The checker always returns one entry per known requirement, even on
/// platforms where it doesn't apply, so downstream code can present a
/// consistent diagnostics view ("Status of all probed requirements:
/// 1 satisfied, 1 not applicable").
/// </para>
/// </summary>
public sealed class DefaultRequirementChecker : IRequirementChecker
{
    /// <summary>
    /// Where to point a user missing the requirement — HIDMaestro's own
    /// repository, which carries the SDK/DLL and setup instructions.
    /// </summary>
    private static readonly Uri HidMaestroInfoUrl = new("https://github.com/hifihedgehog/HIDMaestro");

    private readonly ILogger<DefaultRequirementChecker> logger;

    /// <summary>
    /// Constructs the checker.
    /// </summary>
    public DefaultRequirementChecker(ILogger<DefaultRequirementChecker> logger)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<RequirementStatus>> CheckAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var results = new List<RequirementStatus>
        {
            CheckHidMaestro(),
        };

        // Log a one-line summary so support bundles always carry the
        // outcome of every check.
        var applicable = results.Where(r => r.IsApplicable).ToList();
        var satisfied = applicable.Count(r => r.IsSatisfied);
        logger.LogInformation(
            "Requirement check complete: {SatisfiedCount}/{ApplicableCount} satisfied " +
            "({InapplicableCount} inapplicable on this platform).",
            satisfied,
            applicable.Count,
            results.Count - applicable.Count);

        IReadOnlyList<RequirementStatus> view = results;
        return Task.FromResult(view);
    }

    /// <summary>
    /// Probes for HIDMaestro.Core.dll next to the executable, reusing
    /// the same detection logic the output sink itself uses to decide
    /// whether to activate — one source of truth for "is HIDMaestro
    /// available," not a second, potentially-divergent probe. On
    /// non-Windows platforms returns an inapplicable status so the
    /// dialog hides it from the user.
    /// </summary>
    private RequirementStatus CheckHidMaestro()
    {
        const string id = "hidmaestro";
        const string displayName = "HIDMaestro";
        const string description =
            "Required to create a virtual controller — HIDMaestro is the only output backend. " +
            "Without it, physical controllers will be read normally, but the transformed output " +
            "won't be visible to games or other apps.";

        if (!OperatingSystem.IsWindows())
        {
            return new RequirementStatus(
                Id: id,
                DisplayName: displayName,
                Description: description,
                IsSatisfied: true,
                InstallerUrl: null,
                IsApplicable: false);
        }

        var isSatisfied = HidMaestroDynamic.IsAvailable(logger);

        // Elevation is the other half of "available": the bridge can
        // resolve every type and still be unable to create a single
        // device, because HIDMaestro's driver install and device
        // creation need administrator rights (SeLoadDriverPrivilege).
        // Surface that as an explicit, actionable failure instead of
        // letting the first CreateController produce an opaque access
        // error minutes later.
        var elevationDescription = description;
        if (isSatisfied && !HidMaestroDynamic.IsProcessElevated)
        {
            isSatisfied = false;
            elevationDescription =
                "HIDMaestro is present, but GameFlow is not running as Administrator. " +
                "HIDMaestro needs administrator rights to install its driver and create virtual " +
                "controllers — restart GameFlow as Administrator (right-click → Run as administrator).";
        }

        if (!isSatisfied)
        {
            logger.LogInformation(
                "HIDMaestro requirement unsatisfied ({Status}). User will be offered {InfoUrl}.",
                HidMaestroDynamic.IsProcessElevated ? HidMaestroDynamic.StatusDescription : "process not elevated",
                HidMaestroInfoUrl);
        }

        return new RequirementStatus(
            Id: id,
            DisplayName: displayName,
            Description: elevationDescription,
            IsSatisfied: isSatisfied,
            InstallerUrl: HidMaestroInfoUrl,
            IsApplicable: true);
    }
}
