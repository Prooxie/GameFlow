namespace Autofire.Infrastructure.Requirements;

/// <summary>
/// Outcome of a single startup requirement probe.
///
/// <para>
/// The <see cref="IRequirementChecker"/> emits one of these per requirement
/// it knows about. The startup-checks coordinator consumes the list,
/// decides whether to surface a dialog, and routes the user to the
/// download URL when they accept.
/// </para>
/// </summary>
/// <param name="Id">
/// Stable, machine-readable identifier (e.g. <c>"vigem-bus"</c>). Used in
/// log lines and — eventually — to remember "don't ask me about <c>X</c>"
/// preferences on a per-requirement basis.
/// </param>
/// <param name="DisplayName">
/// Human-readable name for the dialog (e.g. <c>"ViGEm Bus driver"</c>).
/// </param>
/// <param name="Description">
/// One- or two-sentence summary of why this requirement matters. Shown
/// inline in the dialog so the user understands the consequence of
/// declining it.
/// </param>
/// <param name="IsSatisfied">
/// <see langword="true"/> if the requirement is already met (driver
/// installed, library reachable, etc.) and the user does not need to be
/// prompted. <see langword="false"/> means the dialog should offer a
/// download / install action.
/// </param>
/// <param name="InstallerUrl">
/// HTTPS URL the user should be sent to when they choose "install".
/// Typically a GitHub releases page or the upstream vendor's installer.
/// May be <see langword="null"/> if the requirement is platform-only and
/// has no actionable resolution (e.g. on macOS where ViGEm doesn't apply).
/// </param>
/// <param name="IsApplicable">
/// <see langword="false"/> when the requirement is not relevant to the
/// current platform (e.g. ViGEm Bus on Linux). The coordinator filters
/// these out before deciding whether to show the dialog.
/// </param>
public sealed record RequirementStatus(
    string Id,
    string DisplayName,
    string Description,
    bool IsSatisfied,
    Uri? InstallerUrl,
    bool IsApplicable);
