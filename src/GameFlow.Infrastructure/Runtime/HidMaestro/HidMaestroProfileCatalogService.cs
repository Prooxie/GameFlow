using GameFlow.Infrastructure.Runtime.Templates;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// UI-facing view of HIDMaestro's profile catalog. Reads it through
/// <see cref="IHidMaestroBackend"/> so view models can list every profile
/// the loaded SDK actually ships and classify any profile into a kind
/// family for theming, without touching the SDK or blocking the UI thread
/// on the first enumeration (which can take seconds).
///
/// <para>When the backend cannot list a catalog (non-Windows, SDK not
/// present yet) or lists nothing, the curated per-kind defaults are
/// returned instead so the editor still shows the built-in choices
/// rather than an empty list.</para>
/// </summary>
public sealed class HidMaestroProfileCatalogService(
    ILogger<HidMaestroProfileCatalogService> logger,
    IHidMaestroBackend backend)
{
    private readonly ILogger<HidMaestroProfileCatalogService> logger = logger;
    private readonly IHidMaestroBackend backend = backend;
    private readonly object gate = new();
    private Task<IReadOnlyList<HidMaestroCatalogProfile>>? enumeration;

    /// <summary>
    /// The full catalog, enumerated once off the calling thread and
    /// cached. Returns the curated fallback list when the backend can't
    /// list a catalog.
    /// </summary>
    public Task<IReadOnlyList<HidMaestroCatalogProfile>> GetProfilesAsync()
    {
        lock (gate)
        {
            enumeration ??= Task.Run(EnumerateOrFallback);
            return enumeration;
        }
    }

    private IReadOnlyList<HidMaestroCatalogProfile> EnumerateOrFallback()
    {
        try
        {
            if (backend.GetStatus().CanListCatalog)
            {
                var live = backend.GetCatalog();
                if (live.Count > 0)
                {
                    return live;
                }
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "HIDMaestro catalog enumeration failed; using curated defaults.");
        }

        return FallbackProfiles;
    }

    /// <summary>
    /// Classifies a catalog profile id (plus optional display name) into
    /// the nearest kind family — the hook the dashboard theme uses to
    /// follow the selected output controller.
    /// </summary>
    public VirtualControllerKind ClassifyFamily(string? profileId, string? profileName = null) =>
        HidMaestroProfiles.ClassifyFamily(profileId, profileName);

    /// <summary>
    /// True for regular handheld and arcade-style game controllers. The
    /// SDK tags every profile with a type, so the picker needs no name
    /// guessing. Wheels, HOTAS, pedals, mice and keyboards stay in the SDK
    /// catalog, but GameFlow's picker hides them until their extra axes and
    /// feedback have dedicated UI support. A missing type is not a game
    /// controller.
    /// </summary>
    public static bool IsGameControllerProfile(HidMaestroCatalogProfile profile) =>
        profile.Type is not null
        && (profile.Type.Equals("gamepad", StringComparison.OrdinalIgnoreCase)
            || profile.Type.Equals("arcadestick", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The curated defaults shown when the live catalog isn't available:
    /// one verified profile per non-generic kind.
    /// </summary>
    public static IReadOnlyList<HidMaestroCatalogProfile> FallbackProfiles { get; } =
    [
        new("xbox-360-wired",      "Xbox 360 Controller (Wired)", "Microsoft", 0x045E, 0x028E, 10, 5, true, "usb", true),
        new("dualshock-4-v1-full", "DualShock 4",                 "Sony",      0x054C, 0x05C4, 14, 6, true, "usb", true),
        new("dualsense",           "DualSense (PS5)",             "Sony",      0x054C, 0x0CE6, 15, 6, true, "usb", true),
    ];
}
