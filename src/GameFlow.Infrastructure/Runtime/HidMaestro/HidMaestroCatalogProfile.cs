namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// One catalog profile as enumerated from the loaded HIDMaestro SDK.
/// Mirrors the public surface of <c>HMProfile</c> that the UI and
/// theming need (id/name/vendor/identity/shape).
///
/// <para>This is the cross-layer contract between the reflection bridge
/// (<see cref="HidMaestroDynamic"/>), the UI-facing catalog service
/// (<see cref="HidMaestroProfileCatalogService"/>), and the template
/// editor's profile picker — which is why it lives in its own file
/// rather than alongside the bridge.</para>
/// </summary>
public sealed record HidMaestroCatalogProfile(
    string Id,
    string Name,
    string Vendor,
    ushort VendorId,
    ushort ProductId,
    int ButtonCount,
    int AxisCount,
    bool HasHat,
    string Connection,
    bool IsDeployable);
