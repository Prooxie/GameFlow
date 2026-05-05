using Autofire.Infrastructure.Localization;

namespace Autofire.Infrastructure.Runtime;

/// <summary>
/// Catalog of input devices currently visible to the active provider.
///
/// Every status string surfaced from this class — including "No devices found"
/// and the per-provider status text — is resolved through ILocalizationService
/// so it follows the active culture. Previously these were emitted as literal
/// English strings during provider startup, which meant they remained in the
/// language that was active when the provider initialised even after the user
/// switched languages.
/// </summary>
public sealed class InputDeviceCatalog
{
    private readonly ILocalizationService localization;
    private readonly Lock gate = new();

    private IReadOnlyList<DetectedInputDevice> devices = [];
    private string providerStatusKey = "ProviderStatus_NoActiveProvider";
    private object?[] providerStatusArgs = [];
    private string? selectedDeviceId;

    public InputDeviceCatalog(ILocalizationService localization)
    {
        this.localization = localization;
        this.localization.CultureChanged += OnCultureChanged;
    }

    public event EventHandler? Updated;

    public IReadOnlyList<DetectedInputDevice> Devices
    {
        get
        {
            lock (gate)
            {
                return devices;
            }
        }
    }

    public string? SelectedDeviceId
    {
        get
        {
            lock (gate)
            {
                return selectedDeviceId;
            }
        }
    }

    /// <summary>Translated, end-user-facing description of the provider's current state.</summary>
    public string ProviderStatus
    {
        get
        {
            lock (gate)
            {
                return ResolveProviderStatus();
            }
        }
    }

    public void SetSelectedDevice(string? deviceId)
    {
        lock (gate)
        {
            if (string.Equals(selectedDeviceId, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            selectedDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId;
        }

        RaiseUpdated();
    }

    public void Update(IEnumerable<DetectedInputDevice> nextDevices, string statusKey, params object?[] statusArgs)
    {
        lock (gate)
        {
            devices = [.. nextDevices.Distinct(DetectedInputDeviceComparer.Instance)];
            providerStatusKey = string.IsNullOrWhiteSpace(statusKey)
                ? "ProviderStatus_NoActiveProvider"
                : statusKey;
            providerStatusArgs = statusArgs ?? [];
        }

        RaiseUpdated();
    }

    public void Clear(string? statusKey = null)
    {
        lock (gate)
        {
            devices = [];
            providerStatusKey = string.IsNullOrWhiteSpace(statusKey)
                ? "ProviderStatus_NoActiveProvider"
                : statusKey;
            providerStatusArgs = [];
        }

        RaiseUpdated();
    }

    private string ResolveProviderStatus()
    {
        var template = localization[providerStatusKey];
        return providerStatusArgs.Length == 0
            ? template
            : string.Format(template, providerStatusArgs);
    }

    private void OnCultureChanged(object? sender, EventArgs e)
    {
        // Status string is now stale — fire Updated so any UI re-reads it.
        RaiseUpdated();
    }

    private void RaiseUpdated()
    {
        Updated?.Invoke(this, EventArgs.Empty);
    }

    private sealed class DetectedInputDeviceComparer : IEqualityComparer<DetectedInputDevice>
    {
        public static readonly DetectedInputDeviceComparer Instance = new();

        public bool Equals(DetectedInputDevice? x, DetectedInputDevice? y)
        {
            return ReferenceEquals(x, y)
                || (x is not null && y is not null
                    && string.Equals(x.Id, y.Id, StringComparison.OrdinalIgnoreCase));
        }

        public int GetHashCode(DetectedInputDevice obj)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Id);
        }
    }
}

public sealed record DetectedInputDevice(string Id, string DisplayName, string? HardwareId);
