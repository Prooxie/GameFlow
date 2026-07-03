namespace Autofire.Infrastructure.Overlay;

/// <summary>Configuration for the controller overlay server.</summary>
public sealed class OverlayOptions
{
    /// <summary>When false, the overlay server never starts.
    /// Defaulted off: it's an opt-in OBS/streaming feature, and keeping
    /// it dark by default removes an HttpListener + the auto-created
    /// controller-overlays folder from every launch. Re-enable by
    /// setting <c>"Overlay": { "Enabled": true }</c> in appsettings.json.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Localhost port the overlay is served on.</summary>
    public int Port { get; set; } = 8787;
}
