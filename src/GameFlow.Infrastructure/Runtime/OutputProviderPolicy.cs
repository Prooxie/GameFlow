using GameFlow.Infrastructure.Runtime.HidMaestro;
using GameFlow.Infrastructure.Runtime.Templates;

namespace GameFlow.Infrastructure.Runtime;

/// <summary>Resolves saved output identifiers to the platform backend.</summary>
public static class OutputProviderPolicy
{
    /// <summary>Windows virtual HID backend.</summary>
    public const string HidMaestro = "hidmaestro";

    /// <summary>Linux and macOS standard gamepad backend.</summary>
    public const string NativeGamepad = "native-gamepad";

    /// <summary>In-app state without an operating-system device.</summary>
    public const string Preview = "preview";

    private static readonly string[] LegacyProviderIds =
    [
        "vigem-xbox360", "vigem-ds4", "vigem-ds5", "vigem-xbox", "vigem-dualshock4", "vigem-dualsense",
        "xinput", "openxinput", "x360ce", "ps3", "gameinput", "vjoy", "midi",
    ];

    /// <summary>Resolves output for the current operating system.</summary>
    public static string Resolve(string? requestedProviderId) =>
        Resolve(requestedProviderId, OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OperatingSystem.IsMacOS());

    /// <summary>Resolves output for an explicit platform. Unknown platforms retain Preview.</summary>
    public static string Resolve(string? requestedProviderId, bool isWindows, bool isLinux = false, bool isMacOS = false)
    {
        _ = requestedProviderId;
        return isWindows ? HidMaestro : isLinux || isMacOS ? NativeGamepad : Preview;
    }

    /// <summary>Checks whether a saved identifier differs from the current platform backend.</summary>
    public static bool RequiresMigration(string? requestedProviderId) =>
        RequiresMigration(requestedProviderId, OperatingSystem.IsWindows(), OperatingSystem.IsLinux(), OperatingSystem.IsMacOS());

    /// <summary>Checks migration for an explicit platform. Empty identifiers inherit the platform default.</summary>
    public static bool RequiresMigration(string? requestedProviderId, bool isWindows, bool isLinux = false, bool isMacOS = false)
    {
        var normalized = Normalize(requestedProviderId);
        return normalized.Length > 0 && !string.Equals(normalized,
            Resolve(requestedProviderId, isWindows, isLinux, isMacOS), StringComparison.Ordinal);
    }

    /// <summary>Gets output families that the platform backend can create.</summary>
    public static IReadOnlyList<VirtualControllerKind> GetSelectableKinds(bool? isWindows = null) => (isWindows ?? OperatingSystem.IsWindows())
        ? HidMaestroProfiles.SelectableKinds : [VirtualControllerKind.GenericDirectInput];

    /// <summary>Identifies retired providers in saved profiles.</summary>
    public static bool IsLegacyProviderId(string? providerId)
    {
        var normalized = Normalize(providerId);
        return Array.Exists(LegacyProviderIds, id => string.Equals(id, normalized, StringComparison.Ordinal));
    }

    /// <summary>Returns a trimmed identifier in lowercase.</summary>
    public static string Normalize(string? providerId) =>
        string.IsNullOrWhiteSpace(providerId) ? string.Empty : providerId.Trim().ToLowerInvariant();
}
