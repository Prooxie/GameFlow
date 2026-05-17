namespace Autofire.Infrastructure.Requirements;

/// <summary>
/// Probes the local machine for every external dependency the app cares
/// about (driver installs, native libraries) and reports each as a
/// <see cref="RequirementStatus"/>.
///
/// <para>
/// Implementations must:
/// <list type="bullet">
///   <item><description>Be safe to call from any thread.</description></item>
///   <item><description>Never throw — surface failures as a <see cref="RequirementStatus"/>
///   with <c>IsSatisfied = false</c> and a description that explains the
///   failure mode.</description></item>
///   <item><description>Always return one entry per known requirement, with
///   <c>IsApplicable = false</c> for requirements that don't apply to the
///   current platform.</description></item>
/// </list>
/// </para>
/// </summary>
public interface IRequirementChecker
{
    /// <summary>
    /// Runs every known requirement probe and returns the consolidated
    /// list. The list contains both applicable and inapplicable entries
    /// — callers should filter on <see cref="RequirementStatus.IsApplicable"/>
    /// before showing the user anything.
    /// </summary>
    /// <param name="cancellationToken">
    /// Cancellation token honoured by checks that perform I/O (registry
    /// reads, file probes). Pure in-memory checks ignore it.
    /// </param>
    Task<IReadOnlyList<RequirementStatus>> CheckAsync(CancellationToken cancellationToken = default);
}
