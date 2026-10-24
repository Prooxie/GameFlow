using Avalonia.Controls;
using Avalonia.Threading;
using GameFlow.App.ViewModels;
using GameFlow.App.Views;
using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Profiles;
using GameFlow.Infrastructure.Requirements;
using GameFlow.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameFlow.App.Startup;

/// <summary>
/// Runs the requirement and update checks at app startup and drives
/// the corresponding dialogs.
///
/// <para>
/// Lives in the App layer because it uses Avalonia dialogs. Owns the
/// "user said don't ask" and "user said skip this version" persistence
/// so the underlying services (<see cref="IRequirementChecker"/>,
/// <see cref="IUpdateChecker"/>) stay UI-free.
/// </para>
///
/// <para>
/// All of <see cref="RunAsync"/> is wrapped in catch-and-log: a startup
/// check that throws must not prevent the user from reaching the shell.
/// </para>
/// </summary>
public sealed class StartupChecksCoordinator
{
    private readonly IRequirementChecker requirementChecker;
    private readonly IUpdateChecker updateChecker;
    private readonly IUpdateInstaller updateInstaller;
    private readonly IUserSettingsService userSettings;
    private readonly IServiceProvider services;
    private readonly ILogger<StartupChecksCoordinator> logger;

    /// <summary>
    /// Constructs the coordinator.
    /// </summary>
    public StartupChecksCoordinator(
        IRequirementChecker requirementChecker,
        IUpdateChecker updateChecker,
        IUpdateInstaller updateInstaller,
        IUserSettingsService userSettings,
        IServiceProvider services,
        ILogger<StartupChecksCoordinator> logger)
    {
        this.requirementChecker = requirementChecker ?? throw new ArgumentNullException(nameof(requirementChecker));
        this.updateChecker = updateChecker ?? throw new ArgumentNullException(nameof(updateChecker));
        this.updateInstaller = updateInstaller ?? throw new ArgumentNullException(nameof(updateInstaller));
        this.userSettings = userSettings ?? throw new ArgumentNullException(nameof(userSettings));
        this.services = services ?? throw new ArgumentNullException(nameof(services));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs both checks. Safe to invoke once after the shell window
    /// has been shown; both halves are isolated so a failure in one
    /// doesn't block the other. Always returns successfully.
    /// </summary>
    /// <param name="ownerWindow">
    /// The window dialogs are parented to. May be <see langword="null"/>
    /// during very early startup (shouldn't happen in practice).
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation that propagates from app shutdown — both checks
    /// honour it.
    /// </param>
    public async Task RunAsync(Window? ownerWindow, CancellationToken cancellationToken = default)
    {
        try
        {
            await RunRequirementsCheckAsync(ownerWindow, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Requirement startup check threw — continuing.");
        }

        // After requirements, before updates. Requirements first because
        // a missing SDL3 or output backend would make the walkthrough
        // create a controller that cannot work, and the user should hear
        // that from the dialog that explains it. Updates last because
        // "there is a new version" is the least urgent thing a first-time
        // user can be told.
        try
        {
            await RunSetupWalkthroughAsync(ownerWindow, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Setup walkthrough threw — continuing.");
        }

        try
        {
            await RunUpdateCheckAsync(ownerWindow, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Update startup check threw — continuing.");
        }
    }

    /// <summary>
    /// Shows the first-run walkthrough, once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The completion flag is written whether the user finishes or closes
    /// the window. Re-offering onboarding to someone who has already
    /// dismissed it is nagging, and the walkthrough stays available on
    /// demand from Options — so declining it costs nothing that cannot be
    /// recovered deliberately.
    /// </para>
    /// <para>
    /// Skipped when slots already exist. That is the real signal that
    /// someone is set up: the flag is new, so every existing installation
    /// starts with it false, and without this check an established user
    /// would be walked through creating a controller they already have.
    /// </para>
    /// </remarks>
    private async Task RunSetupWalkthroughAsync(Window? ownerWindow, CancellationToken cancellationToken)
    {
        if (userSettings.Current.SetupWalkthroughCompleted)
        {
            return;
        }

        if (ownerWindow is null)
        {
            logger.LogDebug("Setup walkthrough skipped: no owner window.");
            return;
        }

        var registry = services.GetRequiredService<GameFlow.Infrastructure.Runtime.Slots.SlotRegistry>();
        if (registry.GetSlots().Count > 0)
        {
            logger.LogInformation(
                "Setup walkthrough skipped: {Count} controller slot(s) already exist.",
                registry.GetSlots().Count);
            await MarkWalkthroughSeenAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var window = new SetupWalkthroughWindow
            {
                DataContext = services.GetRequiredService<SetupWalkthroughViewModel>(),
            };
            await window.ShowDialog(ownerWindow);
        }).ConfigureAwait(false);

        await MarkWalkthroughSeenAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkWalkthroughSeenAsync(CancellationToken cancellationToken)
    {
        await userSettings.UpdateAsync(
            settings => settings with { SetupWalkthroughCompleted = true }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Half 1: requirements. Skipped entirely when the user has
    /// disabled the check. Shows the dialog only when there are
    /// applicable + unsatisfied requirements.
    /// </summary>
    private async Task RunRequirementsCheckAsync(Window? ownerWindow, CancellationToken cancellationToken)
    {
        if (!userSettings.Current.CheckRequirementsOnStartup)
        {
            logger.LogDebug("Requirement startup check skipped: CheckRequirementsOnStartup is disabled.");
            return;
        }

        var statuses = await requirementChecker.CheckAsync(cancellationToken).ConfigureAwait(false);
        var missing = statuses.Where(s => s.IsApplicable && !s.IsSatisfied).ToList();

        if (missing.Count == 0)
        {
            logger.LogDebug("Requirement startup check found nothing to prompt about.");
            return;
        }

        if (ownerWindow is null)
        {
            logger.LogInformation(
                "Requirement startup check found {MissingCount} missing item(s) but no owner window is available; skipping dialog.",
                missing.Count);
            return;
        }

        var dontAskAgain = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new RequirementsDialog();
            dialog.SetRequirements(missing);
            await dialog.ShowDialog(ownerWindow);
            return dialog.DontAskAgain;
        }).ConfigureAwait(false);

        if (dontAskAgain)
        {
            await userSettings.UpdateAsync(
                settings => settings with { CheckRequirementsOnStartup = false }, cancellationToken).ConfigureAwait(false);
            logger.LogInformation("User opted out of further startup requirement checks.");
        }
    }

    /// <summary>
    /// Half 2: update check + dialog + install hand-off. Pattern-
    /// matches over <see cref="UpdateCheckResult"/> so every shape is
    /// handled.
    /// </summary>
    private async Task RunUpdateCheckAsync(Window? ownerWindow, CancellationToken cancellationToken)
    {
        var result = await updateChecker.CheckAsync(cancellationToken).ConfigureAwait(false);

        switch (result)
        {
            case UpdateCheckResult.Disabled:
            case UpdateCheckResult.UpToDate:
            case UpdateCheckResult.SkippedByUser:
                // Nothing to surface — already logged inside the checker.
                return;

            case UpdateCheckResult.Failed failure:
                logger.LogDebug(
                    failure.Exception,
                    "Update check failed: {Reason}. Continuing without prompting the user.",
                    failure.Reason);
                return;

            case UpdateCheckResult.Available available:
                await PromptForUpdateAsync(ownerWindow, available, cancellationToken).ConfigureAwait(false);
                return;
        }
    }

    /// <summary>
    /// Shows the update dialog and acts on the user's choice.
    /// </summary>
    private async Task PromptForUpdateAsync(
        Window? ownerWindow,
        UpdateCheckResult.Available available,
        CancellationToken cancellationToken)
    {
        if (ownerWindow is null)
        {
            logger.LogInformation(
                "Update {TagName} is available but no owner window is ready; skipping dialog.",
                available.Update.TagName);
            return;
        }

        var choice = await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new UpdateAvailableDialog();
            dialog.SetUpdate(available.Update, available.CurrentVersion);
            dialog.DownloadAsync = async (progress, downloadToken) =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, downloadToken);
                return await updateInstaller.DownloadAndRevealAsync(available.Update, progress, linked.Token);
            };
            await dialog.ShowDialog(ownerWindow);
            return dialog.Choice;
        }).ConfigureAwait(false);

        switch (choice)
        {
            case UpdateDialogChoice.SkipThisUpdate:
                {
                    await userSettings.UpdateAsync(
                        settings => settings with { SkippedUpdateVersion = available.Update.TagName },
                        cancellationToken).ConfigureAwait(false);
                    logger.LogInformation(
                        "User chose to skip update {TagName}.",
                        available.Update.TagName);
                    break;
                }

            case UpdateDialogChoice.DontAskAgain:
                {
                    await userSettings.UpdateAsync(
                        settings => settings with { CheckForUpdatesOnStartup = false }, cancellationToken).ConfigureAwait(false);
                    logger.LogInformation("User opted out of further startup update checks.");
                    break;
                }

            case UpdateDialogChoice.DownloadAndInstall:
                {
                    logger.LogInformation("Update {TagName} was downloaded and verified.", available.Update.TagName);

                    // Clear any previously-skipped version so that if
                    // they install successfully, the next launch
                    // doesn't keep telling them about the same release.
                    await userSettings.UpdateAsync(
                        settings => settings with { SkippedUpdateVersion = null }, cancellationToken).ConfigureAwait(false);

                    break;
                }

            case UpdateDialogChoice.Closed:
                logger.LogDebug("User dismissed the update dialog without choosing.");
                break;
        }
    }
}
