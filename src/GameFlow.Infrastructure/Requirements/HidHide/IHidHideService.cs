namespace GameFlow.Infrastructure.Requirements.HidHide;

/// <summary>
/// Optional HidHide support. HidHide is Nefarius' filter driver that hides
/// physical controllers from games, so a game sees only GameFlow's virtual
/// pad instead of both. GameFlow does not need it to work; the user opts in
/// with <see cref="Profiles.AppSettings.UseHidHide"/>, and only then is it
/// checked for and installed.
/// </summary>
public interface IHidHideService
{
    /// <summary>True on Windows when the user asked for HidHide.</summary>
    bool IsWanted { get; }

    /// <summary>True on Windows; HidHide is a Windows driver.</summary>
    bool IsSupported { get; }

    /// <summary>True when the HidHide driver service is registered.</summary>
    bool IsInstalled();

    /// <summary>
    /// Downloads the pinned official installer, checks its Authenticode
    /// signature and publisher, runs it elevated and waits for it, then
    /// allow-lists GameFlow so it can still read the controllers it hides.
    /// Never throws for expected failures; the outcome says what happened.
    /// </summary>
    Task<HidHideInstallOutcome> InstallAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default);
}

/// <summary>Result of <see cref="IHidHideService.InstallAsync"/>.</summary>
public enum HidHideInstallResult
{
    /// <summary>HidHide is installed and GameFlow is allow-listed.</summary>
    Installed,

    /// <summary>It was already installed; only the allow-list step ran.</summary>
    AlreadyInstalled,

    /// <summary>The installer finished and asks for a restart before the driver loads.</summary>
    RebootRequired,

    /// <summary>The user declined the elevation prompt or cancelled the installer.</summary>
    Cancelled,

    /// <summary>Not Windows.</summary>
    NotSupported,

    /// <summary>Download, signature check or installer failed.</summary>
    Failed,
}

/// <summary>Outcome of an install attempt, with a sentence for the user.</summary>
public sealed record HidHideInstallOutcome(HidHideInstallResult Result, string Message)
{
    /// <summary>True when HidHide is (or will be after a restart) in place.</summary>
    public bool Succeeded => Result is HidHideInstallResult.Installed
        or HidHideInstallResult.AlreadyInstalled
        or HidHideInstallResult.RebootRequired;
}
