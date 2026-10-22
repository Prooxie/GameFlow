using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using HIDMaestro;
using Microsoft.Extensions.Logging;

namespace GameFlow.HidMaestro;

/// <summary>
/// The only owner of the SDK's <c>HMContext</c>. It exists only after the SDK
/// assembly is loaded, so it can name SDK types freely.
/// </summary>
/// <remarks>
/// The backend calls <see cref="TryCreatePad"/> under its lock.
/// <see cref="FinalizeNames"/> runs outside it, because <c>HMContext</c> locks
/// its own controller list.
/// </remarks>
[SupportedOSPlatform("windows")]
internal sealed class HidMaestroSession
{
    private const string FailurePrefix = "HIDMaestro controller creation failed: ";

    private readonly ILogger logger;
    // Not disposed: the SDK's exit handler cleans up, and the old bridge never disposed it either.
    private readonly HMContext context;
    private readonly HidMaestroDriverInitialization driver;
    private bool driverInstallLogged;

    /// <summary>
    /// Creates the context and loads the embedded catalog once. Neither step
    /// needs admin or a driver.
    /// </summary>
    public HidMaestroSession(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        this.logger = logger;

        context = new HMContext();
        _ = context.LoadDefaultProfiles();

        var issues = context.ProfileLoadIssues.Count;
        if (issues > 0)
        {
            logger.LogWarning(
                "HIDMaestro: the embedded catalog refused or flagged {Count} profile(s); those profiles may be missing.",
                issues);
        }

        // Catalog discovery is read-only. InstallDriver sweeps the machine,
        // so it runs only when the first pad is created.
        driver = new HidMaestroDriverInitialization(context.InstallDriver);
        SdkVersion = typeof(HMContext).Assembly.GetName().Version ?? new Version(0, 0);
        Catalog = ProjectCatalog(context.AllProfiles, logger);
    }

    /// <summary>The version of the loaded SDK assembly.</summary>
    public Version SdkVersion { get; }

    /// <summary>Every loaded profile, sorted by vendor and then by name. Projected once.</summary>
    public IReadOnlyList<HidMaestroCatalogProfile> Catalog { get; }

    /// <summary>
    /// Installs the driver on first use, then creates one pad. Returns false
    /// with a reason when any step fails.
    /// </summary>
    /// <remarks>
    /// Not inlined: the backend is compiled before the SDK may exist, so no
    /// SDK type may reach its methods through an inlined call.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool TryCreatePad(HidMaestroPadRequest request, [NotNullWhen(true)] out IHidMaestroPad? pad, out string? failure)
    {
        ArgumentNullException.ThrowIfNull(request);
        pad = null;

        if (!driver.TryEnsureInstalled(out failure))
        {
            return false;
        }

        if (!driverInstallLogged)
        {
            driverInstallLogged = true;
            logger.LogInformation("HIDMaestro: driver install finished ({Outcome}).", context.LastDriverInstall);
        }

        HMController? controller = null;
        try
        {
            HMProfile profile;
            if (request.Generic is { } shape)
            {
                profile = HidMaestroGenericProfile.Build(request.ProfileId, shape);
            }
            else if (GetProfile(request.ProfileId) is { } found)
            {
                profile = found;
            }
            else
            {
                failure = $"{FailurePrefix}profile '{request.ProfileId}' is not in the HIDMaestro catalog";
                return false;
            }

            controller = context.CreateController(profile);
            pad = new HidMaestroPad(controller, profile, logger);
            return true;
        }
        catch (Exception exception)
        {
            // The device exists once CreateController returns. Remove it, or
            // each failed attempt leaves a ghost pad until the process exits.
            if (controller is not null)
            {
                TryDispose(controller);
            }

            failure = DescribeFailure(exception);
            logger.LogError(exception, "HIDMaestro controller creation failed for profile {Profile}.", request.ProfileId);
            return false;
        }
    }

    /// <summary>
    /// Gives every pad its final Windows name. Call it once after a rebuild
    /// has created all its pads: a later pad's driver bind can rename an
    /// earlier one.
    /// </summary>
    /// <remarks>
    /// Best effort. A failure leaves the names Windows chose, which is not
    /// worth failing a rebuild over. Not inlined, for the reason given on
    /// <see cref="TryCreatePad"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void FinalizeNames()
    {
        try
        {
            context.FinalizeNames();
        }
        catch (Exception exception)
        {
            logger.LogDebug(
                exception, "HIDMaestro: FinalizeNames() failed; virtual controller names may show the driver default.");
        }
    }

    /// <summary>The loaded profile with this id, or null when the catalog has none.</summary>
    internal HMProfile? GetProfile(string id) => context.GetProfile(id);

    /// <summary>Maps one SDK profile onto the catalog record the UI uses.</summary>
    /// <remarks>
    /// <c>HasExtendedInput</c> is the SDK's literal value: the profile declares an
    /// extended report. A pad uses <see cref="CanArmExtendedInput"/> instead.
    /// </remarks>
    internal static HidMaestroCatalogProfile Project(HMProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return new HidMaestroCatalogProfile(
            Id: profile.Id,
            Name: string.IsNullOrEmpty(profile.Name) ? profile.Id : profile.Name,
            Vendor: profile.Vendor ?? string.Empty,
            VendorId: profile.VendorId,
            ProductId: profile.ProductId,
            ButtonCount: profile.ButtonCount,
            AxisCount: profile.AxisCount,
            HasHat: profile.HasHat,
            Connection: profile.Connection,
            IsDeployable: profile.IsDeployable,
            Type: profile.Type,
            HasExtendedInput: profile.HasExtendedInput);
    }

    /// <summary>
    /// Tells whether the profile can arm its extended input report. Battery,
    /// motion and touch reach the wire only through an armed report. A USB
    /// DualSense or DualShock 4 declares one but never arms it.
    /// </summary>
    internal static bool CanArmExtendedInput(HMProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        try
        {
            return ReadArmableExtendedInput(profile);
        }
        catch (Exception exception) when (exception is MemberAccessException or TypeLoadException)
        {
            // MemberAccessException covers a removed member (MissingMemberException)
            // and one made less visible (MethodAccessException, FieldAccessException).
            // No log: this can run for each pad, and the fallback is a safe answer.
            return profile.HasExtendedInput;
        }
    }

    /// <summary>
    /// Reads the arm settings from <c>HIDMaestro.Internal.ExtendedReportSpec</c>,
    /// which the SDK may change in a minor release. Not inlined, so a changed
    /// member fails here, inside the caller's try block.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ReadArmableExtendedInput(HMProfile profile) =>
        profile.ExtendedReport is { } report && (report.AlwaysArmed || report.ArmOn is { Count: > 0 });

    /// <summary>
    /// Turns a creation exception into one line for the user. Every text says
    /// "failed", and an access problem says "Administrator", because the
    /// dashboard matches those words.
    /// </summary>
    internal static string DescribeFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            UnauthorizedAccessException => FailurePrefix + "access denied — run GameFlow as Administrator.",
            HMResourceInUseException inUse =>
                $"{FailurePrefix}in use by {inUse.OwnerImagePath ?? "another program"} " +
                $"(process {inUse.OwnerProcessId?.ToString() ?? "unknown"}).",
            HMPayloadMissingException or HMDriverVersionException or HMProfileException =>
                FailurePrefix + exception.Message,
            _ => $"{FailurePrefix}{exception.GetType().Name}: {exception.Message}",
        };
    }

    /// <summary>
    /// Projects every profile and sorts by vendor, then name. A profile the
    /// SDK cannot describe is skipped, so one bad entry cannot hide the rest.
    /// </summary>
    private static HidMaestroCatalogProfile[] ProjectCatalog(IReadOnlyList<HMProfile> profiles, ILogger logger)
    {
        var projected = new List<HidMaestroCatalogProfile>(profiles.Count);
        foreach (var profile in profiles)
        {
            try
            {
                projected.Add(Project(profile));
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "HIDMaestro: catalog profile {Id} could not be read; it is skipped.", profile.Id);
            }
        }

        // A stable sort: profiles with the same vendor and name keep the SDK's id order.
        return projected
            .OrderBy(p => p.Vendor, StringComparer.OrdinalIgnoreCase)
            .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void TryDispose(HMController controller)
    {
        try
        {
            controller.Dispose();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "HIDMaestro: a half-created controller could not be removed.");
        }
    }
}
