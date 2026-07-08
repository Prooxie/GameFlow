using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Requirements;

/// <summary>
/// Default <see cref="IRequirementChecker"/>. Today it knows about a
/// single requirement (the ViGEm Bus driver, Windows-only); add new
/// requirements here as the input/output sink list grows in step 5 of
/// they land (HIDMaestro driver presence is the next candidate).
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
    /// The canonical install URL for the ViGEm Bus driver, per the
    /// upstream project README. Constant rather than configurable
    /// because it's a third-party stable URL.
    /// </summary>
    private static readonly Uri ViGEmBusInstallerUrl = new("https://github.com/nefarius/ViGEmBus/releases");

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
            CheckViGEmBus(),
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
    /// Probes for the ViGEm Bus driver. On non-Windows platforms returns
    /// an inapplicable status so the dialog hides it from the user.
    /// </summary>
    private RequirementStatus CheckViGEmBus()
    {
        const string id = "vigem-bus";
        const string displayName = "ViGEm Bus driver";
        const string description =
            "Required to create virtual Xbox 360, DualShock 4 or DualSense controllers. " +
            "Without it, only the Preview output is available — physical controllers will be read normally, but the " +
            "transformed output won't be visible to other apps.";

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

        var detection = ViGEmBusDetection.Detect(logger);
        var isSatisfied = detection == ViGEmBusDetection.Detection.Installed;

        if (!isSatisfied)
        {
            logger.LogInformation(
                "ViGEm Bus driver appears to be missing (probe result: {ProbeResult}). " +
                "User will be offered the installer at {InstallerUrl}.",
                detection,
                ViGEmBusInstallerUrl);
        }

        return new RequirementStatus(
            Id: id,
            DisplayName: displayName,
            Description: description,
            IsSatisfied: isSatisfied,
            InstallerUrl: ViGEmBusInstallerUrl,
            IsApplicable: true);
    }
}
