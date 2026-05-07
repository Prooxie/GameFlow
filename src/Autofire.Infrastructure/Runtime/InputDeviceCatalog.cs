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
    private IReadOnlyList<(ushort Vid, ushort Pid)> ignoredHardwareSignatures = [];
    private string? selectedDeviceId;

    /// <summary>
    /// Provider-status localization key (or English literal — the localizer
    /// falls through to the input string when no resource is found, so input
    /// sources that don't yet have a translated key still display their text).
    /// Resolved through <see cref="ILocalizationService"/> on every read so
    /// that culture changes are reflected immediately without callers having
    /// to push a new value.
    /// </summary>
    private string? statusKey;

    /// <summary>
    /// Format arguments interpolated into the resolved <see cref="statusKey"/>
    /// string when it contains <c>{0}</c>-style placeholders.
    /// </summary>
    private object?[] statusArgs = [];

    /// <summary>
    /// Localization key consulted when <see cref="statusKey"/> is null
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
                        .Where(d => !MatchesIgnoredHardwareSignature(d))
                        .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
                        .Select(g => g.First())
                ];
            }
        }

        RaiseUpdated();
    }

    /// <summary>
    /// Sets the provider-status string. <paramref name="keyOrLiteral"/> is
    /// resolved through <see cref="ILocalizationService"/> on every read so
    /// culture changes are reflected without callers having to push a new
    /// value. If no resource matches, the localizer falls through to the
    /// input string verbatim — making this method safe to call with either
    /// a known localization key (e.g. <c>ProviderStatus_XInputActive</c>) or
    /// an English literal during incremental localization rollout.
    /// </summary>
    /// <param name="keyOrLiteral">
    /// The localization key (preferred) or English literal to display. Pass
    /// null or empty to revert to the localized default
    /// (<c>ProviderStatus_NoActiveProvider</c>).
    /// </param>
    /// <param name="args">
    /// Optional <see cref="string.Format(string, object?[])"/> arguments
    /// interpolated into the resolved string. Use these for keys whose
    /// translation contains <c>{0}</c>-style placeholders, e.g.
    /// <c>"ProviderStatus_XInputActive"</c> + <c>controllerCount</c>.
    /// </param>
    public void SetProviderStatus(string? keyOrLiteral, params object?[] args)
    {
        var normalisedKey  = string.IsNullOrWhiteSpace(keyOrLiteral) ? null : keyOrLiteral;
        var normalisedArgs = args ?? [];

        lock (gate)
        {
            if (string.Equals(statusKey, normalisedKey, StringComparison.Ordinal)
                && ArgsEqual(statusArgs, normalisedArgs))
            {
                return;
            }
            statusKey  = normalisedKey;
            statusArgs = normalisedArgs;
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
    /// Replaces the set of (vendor id, product id) pairs that should be
    /// hidden from <see cref="Devices"/>. The runtime uses this to hide its
    /// own virtual output device (e.g. a ViGEm Xbox 360 controller advertises
    /// VID 0x045E PID 0x028E and would otherwise re-appear in the input
    /// source dropdown, creating a confusing self-referential entry).
    ///
    /// A real Xbox/DS4 plugged into the same machine matches the same
    /// signature — so this filter only fires when the user has explicitly
    /// chosen the matching virtual output sink.
    /// </summary>
    /// <param name="signatures">
    /// VID/PID pairs to ignore. Pass an empty collection to clear the filter.
    /// </param>
    public void SetIgnoredHardwareSignatures(IEnumerable<(ushort Vid, ushort Pid)>? signatures)
    {
        var next = (signatures ?? []).Distinct().ToArray();

        bool changed;
        lock (gate)
        {
            changed = !ignoredHardwareSignatures.SequenceEqual(next);
            ignoredHardwareSignatures = next;

            if (changed && devices.Count > 0)
            {
                // Re-apply the filter to the existing list.
                devices = [.. devices.Where(d => !MatchesIgnoredHardwareSignature(d))];
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
    /// <param name="statusKeyOrLiteral">
    /// Optional localization key or English literal describing why the catalog
    /// was cleared. Pass null to revert to the localized default ("no active
    /// provider").
    /// </param>
    /// <param name="args">Format arguments for the status string, when applicable.</param>
    public void Clear(string? statusKeyOrLiteral = null, params object?[] args)
    {
        var normalisedKey  = string.IsNullOrWhiteSpace(statusKeyOrLiteral) ? null : statusKeyOrLiteral;
        var normalisedArgs = args ?? [];

        lock (gate)
        {
            devices    = [];
            statusKey  = normalisedKey;
            statusArgs = normalisedArgs;
        }

        RaiseUpdated();
    }

    /// <summary>
    /// Resolves the user-visible provider status. Must be called under <see cref="gate"/>.
    /// </summary>
    /// <returns>The fully-localized status string (or the literal text if no resource matched).</returns>
    private string ResolveProviderStatus()
    {
        var key      = statusKey ?? defaultStatusKey;
        var template = localization[key];

        if (statusArgs.Length == 0)
        {
            return template;
        }

        // Best-effort string.Format. If the template is a literal that doesn't
        // have placeholders we return it unchanged; if string.Format throws on
        // a malformed template we surface the raw template rather than crash.
        try
        {
            return string.Format(System.Globalization.CultureInfo.CurrentCulture, template, statusArgs);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    /// <summary>
    /// True when <paramref name="device"/> matches one of the registered
    /// (vendor id, product id) pairs in <see cref="ignoredHardwareSignatures"/>.
    /// </summary>
    /// <param name="device">The device to check.</param>
    /// <returns>True if the device is hardware-signature-filtered.</returns>
    private bool MatchesIgnoredHardwareSignature(InputDeviceInfo device)
    {
        if (ignoredHardwareSignatures.Count == 0 || (device.VendorId == 0 && device.ProductId == 0))
        {
            return false;
        }

        for (var i = 0; i < ignoredHardwareSignatures.Count; i++)
        {
            var sig = ignoredHardwareSignatures[i];
            if (sig.Vid == device.VendorId && sig.Pid == device.ProductId)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Reference-and-value-equality compare for the status args array.</summary>
    /// <param name="a">First args array.</param>
    /// <param name="b">Second args array.</param>
    /// <returns>True if both arrays carry the same values in the same order.</returns>
    private static bool ArgsEqual(object?[] a, object?[] b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }
        if (a.Length != b.Length)
        {
            return false;
        }
        for (var i = 0; i < a.Length; i++)
        {
            if (!Equals(a[i], b[i]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Reacts to a culture change by re-raising <see cref="Updated"/> so any UI
    /// re-reads the (now possibly different) localized status.
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
