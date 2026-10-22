namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// One catalog profile as enumerated from the loaded HIDMaestro SDK.
/// Mirrors the public surface of <c>HMProfile</c> that the UI and
/// theming need (id/name/vendor/identity/shape).
///
/// <para>This is the cross-layer contract between the backend that
/// projects it (<see cref="IHidMaestroBackend.GetCatalog"/>), the output
/// sink that resolves profile ids and reserved signatures against it,
/// the UI-facing catalog service
/// (<see cref="HidMaestroProfileCatalogService"/>), and the template
/// editor's profile picker. It names no SDK type, so it can live in
/// Infrastructure, which builds without the SDK.</para>
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
    bool IsDeployable,
    string Type = "gamepad",
    bool HasExtendedInput = false);
