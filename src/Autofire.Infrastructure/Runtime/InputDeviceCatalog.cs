using Autofire.Infrastructure.Localization;

namespace Autofire.Infrastructure.Runtime;

/// <summary>
/// Catalog of input devices currently visible to the active provider.
///
/// History note: this type was originally built around the 3-field
/// <see cref="DetectedInputDevice"/> record and a localization-key-based
/// status string. The input source layer has since standardised on the
/// richer <see cref="InputDeviceInfo"/> record (which carries connected /
/// selected flags and VID/PID identifiers) and on literal-string status
/// text. The catalog has been migrated to that newer model. The legacy
/// <see cref="DetectedInputDevice"/> record is intentionally kept on disk
/// because the work-in-progress IInputProvider files (currently excluded
/// from compilation) still reference it.
///
/// Localization regression: provider-status strings used to be resource
/// keys resolved through <see cref="ILocalizationService"/>. The input
/// sources are now passing English literals directly. We preserve the
/// "ProviderStatus_NoActiveProvider" default-state localization, but
/// status strings produced at runtime are surfaced verbatim. Re-localizing
/// these is on the future-work list.
/// </summary>
public sealed class InputDeviceCatalog
{
    private readonly ILocalizationService localization;
    private readonly Lock gate = new();

    private IReadOnlyList<InputDeviceInfo> devices = [];
    private IReadOnlySet<string> ignoredDeviceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private string? selectedDeviceId;

    /// <summary>
    /// Free-form provider status string. When non-null, takes precedence
    /// over <see cref="defaultStatusKey"/>. Set by
    /// <see cref="SetProviderStatus(string)"/>.
    /// </summary>
    private string? literalProviderStatus;

    /// <summary>
    /// Localization key consulted when no literal status has been set
    /// (i.e. the catalog has just been cleared or freshly created).
    /// </summary>
    private readonly string defaultStatusKey = "ProviderStatus_NoActiveProvider";

    /// <param name="localization">Localization service used to resolve the catalog's
    /// idle/no-provider status string.</param>
    public InputDeviceCatalog(ILocalizationService localization)
    {
        this.localization = localization;
        this.localization.CultureChanged += OnCultureChanged;
    }

    /// <summary>Raised whenever the catalog's device list, status, or selection changes.</summary>
    public event EventHandler? Updated;

    /// <summary>Snapshot of the currently visible input devices, with ignored ids filtered out.</summary>
    public IReadOnlyList<InputDeviceInfo> Devices
    {
        get
        {
            lock (gate)
            {
                return devices;
            }
        }
    }

    /// <summary>Identifier of the currently selected device, or null for "automatic selection".</summary>
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

    /// <summary>End-user-facing description of the active provider's current state.</summary>
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

    /// <summary>
    /// Marks the given device id as currently selected. Pass null to revert to
    /// automatic selection.
    /// </summary>
    /// <param name="deviceId">The device id to mark selected, or null/empty for auto.</param>
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

    /// <summary>
    /// Replaces the catalog's known device list. Devices whose ids are in the
    /// current ignore list are filtered out. Duplicates by id are coalesced
    /// (case-insensitive). Does NOT affect <see cref="ProviderStatus"/>.
    /// </summary>
    /// <param name="nextDevices">The new device list. May be null or empty.</param>
    public void ReplaceDevices(IReadOnlyList<InputDeviceInfo>? nextDevices)
    {
        lock (gate)
        {
            if (nextDevices is null || nextDevices.Count == 0)
            {
                if (devices.Count == 0)
                {
                    return;
                }
                devices = [];
            }
            else
            {
                devices =
                [
                    .. nextDevices
                        .Where(d => !ignoredDeviceIds.Contains(d.Id))
                        .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                ];
            }
        }

        RaiseUpdated();
    }

    /// <summary>
    /// Sets the provider's free-form status string. The string is surfaced
    /// verbatim by <see cref="ProviderStatus"/>; the localization layer is
    /// not consulted. Pass null or empty to revert to the localized default.
    /// </summary>
    /// <param name="status">The status text, or null/empty to clear.</param>
    public void SetProviderStatus(string? status)
    {
        lock (gate)
        {
            var normalised = string.IsNullOrWhiteSpace(status) ? null : status;
            if (string.Equals(literalProviderStatus, normalised, StringComparison.Ordinal))
            {
                return;
            }
            literalProviderStatus = normalised;
        }

        RaiseUpdated();
    }

    /// <summary>
    /// Replaces the set of device ids that should be hidden from
    /// <see cref="Devices"/>. Useful for filtering the runtime's own virtual
    /// output device out of the input source selection list.
    /// </summary>
    /// <param name="ids">Device ids to ignore (case-insensitive). Pass an empty
    /// collection to clear the filter.</param>
    public void SetIgnoredDeviceIds(IEnumerable<string>? ids)
    {
        var next = new HashSet<string>(
            ids?.Where(id => !string.IsNullOrWhiteSpace(id)) ?? [],
            StringComparer.OrdinalIgnoreCase);

        bool changed;
        lock (gate)
        {
            changed = !next.SetEquals(ignoredDeviceIds);
            ignoredDeviceIds = next;

            if (changed && devices.Count > 0)
            {
                // Re-apply the filter to the existing list.
                devices = [.. devices.Where(d => !ignoredDeviceIds.Contains(d.Id))];
            }
        }

        if (changed)
        {
            RaiseUpdated();
        }
    }

    /// <summary>
    /// Resets the catalog: empties the device list and updates the status.
    /// </summary>
    /// <param name="status">Optional literal status text describing why the
    /// catalog was cleared. When null, the catalog reverts to the localized
    /// default ("no active provider").</param>
    public void Clear(string? status = null)
    {
        lock (gate)
        {
            devices = [];
            literalProviderStatus = string.IsNullOrWhiteSpace(status) ? null : status;
        }

        RaiseUpdated();
    }

    /// <summary>
    /// Resolves the user-visible provider status. Must be called under <see cref="gate"/>.
    /// </summary>
    /// <returns>The literal status if one is set, otherwise the localized default.</returns>
    private string ResolveProviderStatus()
    {
        return literalProviderStatus ?? localization[defaultStatusKey];
    }

    /// <summary>
    /// Reacts to a culture change by re-raising <see cref="Updated"/> so any UI
    /// re-reads the (now possibly different) localized default status.
    /// </summary>
    private void OnCultureChanged(object? sender, EventArgs e)
    {
        RaiseUpdated();
    }

    /// <summary>Fires the <see cref="Updated"/> event.</summary>
    private void RaiseUpdated()
    {
        Updated?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Legacy 3-field input device record. Retained for source-compatibility with
/// the work-in-progress Runtime/Providers/* files that are currently
/// excluded from compilation. New code should use <see cref="InputDeviceInfo"/>.
/// </summary>
[Obsolete("Use InputDeviceInfo. Retained only so the excluded WIP provider files remain on-disk-valid.")]
public sealed record DetectedInputDevice(string Id, string DisplayName, string? HardwareId);
