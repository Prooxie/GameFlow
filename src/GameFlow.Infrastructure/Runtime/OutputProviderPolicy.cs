namespace GameFlow.Infrastructure.Runtime;

/// <summary>
/// The single source of truth for which output backend a provider id
/// resolves to. On Windows there is exactly ONE output backend —
/// HIDMaestro — so every id (current, legacy, empty, or unknown)
/// resolves to it. On non-Windows platforms, where HIDMaestro cannot
/// run, everything resolves to the in-app preview sink so the runtime
/// keeps ticking and the dashboard keeps rendering.
///
/// <para>
/// Centralised here (rather than spread across the sink factory, the
/// slot registry's load-time migration, and the view models) so the
/// rule can never drift between layers: the factory uses it to pick a
/// sink, the registry uses it to migrate persisted slot files, and the
/// UI uses it to decide which options to even offer.
/// </para>
/// </summary>
public static class OutputProviderPolicy
{
    /// <summary>Provider id of the sole real (Windows) output backend.</summary>
    public const string HidMaestro = "hidmaestro";

    /// <summary>Provider id of the non-Windows / diagnostics fallback.</summary>
    public const string Preview = "preview";

    /// <summary>
    /// Legacy ids from retired backends. Kept only so persisted profiles
    /// and slot files written by older builds migrate loudly instead of
    /// resolving to nothing.
    /// </summary>
    private static readonly string[] LegacyProviderIds =
    [
        "vigem-xbox360", "vigem-ds4", "vigem-ds5",
        "vigem-xbox", "vigem-dualshock4", "vigem-dualsense",
        "xinput", "openxinput", "x360ce", "ps3", "gameinput", "vjoy", "midi",
    ];

    /// <summary>
    /// Resolves any requested provider id to the backend this build
    /// actually runs on the current platform. Never returns an unknown
    /// id: Windows → <see cref="HidMaestro"/>, everything else →
    /// <see cref="Preview"/>.
    /// </summary>
    public static string Resolve(string? requestedProviderId) =>
        Resolve(requestedProviderId, OperatingSystem.IsWindows());

    /// <summary>
    /// Platform-explicit overload for tests and callers that already
    /// know the platform.
    /// </summary>
    public static string Resolve(string? requestedProviderId, bool isWindows)
    {
        _ = Normalize(requestedProviderId); // validates/normalises; result independent of it by design
        return isWindows ? HidMaestro : Preview;
    }

    /// <summary>
    /// True when <paramref name="requestedProviderId"/> is something other
    /// than what <see cref="Resolve(string?, bool)"/> returns for it —
    /// i.e. the caller should log/persist a migration. Empty and
    /// whitespace ids count as "was different" only when they carry no
    /// information (callers treat empty as "inherit", which is quieter).
    /// </summary>
    public static bool RequiresMigration(string? requestedProviderId, bool isWindows)
    {
        var normalized = Normalize(requestedProviderId);
        if (normalized.Length == 0)
        {
            return false; // empty = "inherit"; backfill is a separate, quieter path
        }

        return !string.Equals(normalized, Resolve(requestedProviderId, isWindows), StringComparison.Ordinal);
    }

    /// <summary>True for ids of backends retired from this codebase entirely.</summary>
    public static bool IsLegacyProviderId(string? providerId)
    {
        var normalized = Normalize(providerId);
        return Array.Exists(LegacyProviderIds, id => string.Equals(id, normalized, StringComparison.Ordinal));
    }

    /// <summary>Trimmed, lower-cased id; empty string for null/whitespace.</summary>
    public static string Normalize(string? providerId) =>
        string.IsNullOrWhiteSpace(providerId) ? string.Empty : providerId.Trim().ToLowerInvariant();
}
