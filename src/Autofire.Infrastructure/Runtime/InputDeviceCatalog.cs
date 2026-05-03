namespace Autofire.Infrastructure.Runtime;

/// <summary>
/// Thread-safe registry of detected input devices and provider status text.
///
/// The <see cref="Changed"/> event is debounced: it fires at most once per
/// <see cref="DebounceMs"/> milliseconds regardless of how often the underlying
/// data changes. This prevents background polling loops from flooding the
/// Avalonia UI dispatcher with notifications.
/// </summary>
public sealed class InputDeviceCatalog : IDisposable
{
    private const int DebounceMs = 800;

    private readonly Lock syncRoot = new();
    private readonly System.Threading.Timer debounceTimer;
    private IReadOnlyList<InputDeviceInfo> rawDevices = [];
    private IReadOnlyList<InputDeviceInfo> devices = [];
    private HashSet<string> ignoredDeviceIds = new(StringComparer.OrdinalIgnoreCase);
    private string? selectedDeviceId;
    private string providerStatus = "No live input selected.";
    private bool pendingChange;
    private bool disposed;

    public InputDeviceCatalog()
    {
        debounceTimer = new System.Threading.Timer(
            _ => FirePendingChange(),
            state: null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    public event EventHandler? Changed;

    public IReadOnlyList<InputDeviceInfo> Devices
    {
        get { lock (syncRoot) { return devices; } }
    }

    public string? SelectedDeviceId
    {
        get { lock (syncRoot) { return selectedDeviceId; } }
    }

    public string ProviderStatus
    {
        get { lock (syncRoot) { return providerStatus; } }
    }

    public void SetProviderStatus(string? status)
    {
        var normalized = string.IsNullOrWhiteSpace(status)
            ? "Provider status unavailable."
            : status.Trim();

        lock (syncRoot)
        {
            if (string.Equals(providerStatus, normalized, StringComparison.Ordinal))
            {
                return;
            }

            providerStatus = normalized;
            ScheduleChange();
        }
    }

    public void ReplaceDevices(IEnumerable<InputDeviceInfo> sourceDevices)
    {
        var list = sourceDevices
            .Where(d => !string.IsNullOrWhiteSpace(d.Id))
            .GroupBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .OrderBy(d => d.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        lock (syncRoot)
        {
            rawDevices = list;
            if (RebuildVisibleDevicesNoLock())
            {
                ScheduleChange();
            }
        }
    }

    public void SetSelectedDevice(string? deviceId)
    {
        var normalized = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();

        lock (syncRoot)
        {
            if (!string.IsNullOrWhiteSpace(normalized) &&
                devices.All(d => !string.Equals(d.Id, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                normalized = null;
            }

            if (string.Equals(selectedDeviceId, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            selectedDeviceId = normalized;
            if (RebuildVisibleDevicesNoLock())
            {
                ScheduleChange();
            }
        }
    }

    public void SetIgnoredDeviceIds(IEnumerable<string>? deviceIds)
    {
        var normalized = deviceIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        lock (syncRoot)
        {
            if (ignoredDeviceIds.SetEquals(normalized))
            {
                return;
            }

            ignoredDeviceIds = normalized;
            if (RebuildVisibleDevicesNoLock())
            {
                ScheduleChange();
            }
        }
    }

    public void Clear(string? status = null)
    {
        lock (syncRoot)
        {
            rawDevices = [];
            devices = [];
            ignoredDeviceIds.Clear();
            selectedDeviceId = null;
            providerStatus = string.IsNullOrWhiteSpace(status)
                ? "No live input selected."
                : status.Trim();
            ScheduleChange();
        }
    }

    private bool RebuildVisibleDevicesNoLock()
    {
        var visible = rawDevices
            .Where(d => !ignoredDeviceIds.Contains(d.Id))
            .ToList();

        var previousSelectedId = selectedDeviceId;
        if (!string.IsNullOrWhiteSpace(selectedDeviceId) &&
            visible.All(d => !string.Equals(d.Id, selectedDeviceId, StringComparison.OrdinalIgnoreCase)))
        {
            selectedDeviceId = null;
        }

        if (string.IsNullOrWhiteSpace(selectedDeviceId) && visible.Count == 1)
        {
            selectedDeviceId = visible[0].Id;
        }

        var normalized = visible
            .Select(d => d with
            {
                IsSelected = !string.IsNullOrWhiteSpace(selectedDeviceId) &&
                             string.Equals(d.Id, selectedDeviceId, StringComparison.OrdinalIgnoreCase)
            })
            .ToArray();

        var changed = previousSelectedId != selectedDeviceId || !AreEqual(devices, normalized);
        devices = normalized;
        return changed;
    }

    private static bool AreEqual(IReadOnlyList<InputDeviceInfo> left, IReadOnlyList<InputDeviceInfo> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private void ScheduleChange()
    {
        pendingChange = true;
        _ = debounceTimer.Change(DebounceMs, Timeout.Infinite);
    }

    private void FirePendingChange()
    {
        bool fire;
        lock (syncRoot)
        {
            fire = pendingChange;
            pendingChange = false;
        }

        if (fire && !disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        debounceTimer.Dispose();
    }
}
