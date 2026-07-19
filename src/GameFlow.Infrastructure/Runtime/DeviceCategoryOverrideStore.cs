using System.Text.Json;
using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Profiles;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime;

/// <summary>
/// Persists per-device <see cref="DeviceCategory"/> OVERRIDES to
/// <see cref="AppPaths.DeviceCategoryOverridesFile"/> — the "treat this
/// as a gamepad" escape hatch for devices Windows/SDL/Raw Input reports
/// under the wrong category (a HID-only wheel or flight stick enumerated
/// as "Unknown", a gamepad-shaped device that SDL doesn't recognize and
/// so falls to Keyboard/Mouse detection, or the reverse — a keyboard or
/// mouse GameFlow's own heuristics misclassify as a gamepad).
///
/// <para>Applied once, centrally, in
/// <see cref="InputDeviceCatalog.MergeSources"/> — every consumer
/// (dashboard panels, slot device lists, theme resolution) sees the
/// corrected category uniformly, rather than each screen needing its own
/// override lookup.</para>
/// </summary>
public sealed class DeviceCategoryOverrideStore
{
    private readonly ILogger<DeviceCategoryOverrideStore> logger;
    private readonly Lock gate = new();
    private Dictionary<string, DeviceCategory> overrides = new(StringComparer.Ordinal);

    public DeviceCategoryOverrideStore(ILogger<DeviceCategoryOverrideStore> logger)
    {
        this.logger = logger;
        Load();
    }

    /// <summary>Raised after an override is set or cleared (device id payload) — catalog consumers should re-merge.</summary>
    public event EventHandler<string>? OverrideChanged;

    /// <summary>The override for a device, or null if the device uses its detected category as-is.</summary>
    public DeviceCategory? GetOrNull(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return null;
        }
        lock (gate)
        {
            return overrides.TryGetValue(deviceId, out var category) ? category : null;
        }
    }

    /// <summary>Sets (or, for <see cref="DeviceCategory.Unknown"/>, clears) the override and persists.</summary>
    public void Set(string deviceId, DeviceCategory category)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return;
        }

        lock (gate)
        {
            if (category == DeviceCategory.Unknown)
            {
                overrides.Remove(deviceId);
            }
            else
            {
                overrides[deviceId] = category;
            }
            Persist();
        }
        OverrideChanged?.Invoke(this, deviceId);
    }

    /// <summary>Clears the override for a device (equivalent to <c>Set(deviceId, DeviceCategory.Unknown)</c>).</summary>
    public void Clear(string deviceId) => Set(deviceId, DeviceCategory.Unknown);

    /// <summary>Applies any stored override to a device, returning it unchanged if none exists.</summary>
    public InputDeviceInfo Apply(InputDeviceInfo device)
    {
        var overridden = GetOrNull(device.Id);
        return overridden is null || overridden == device.Category
            ? device
            : device with { Category = overridden.Value };
    }

    private void Load()
    {
        try
        {
            var path = AppPaths.DeviceCategoryOverridesFile;
            if (!File.Exists(path))
            {
                return;
            }
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, DeviceCategory>>(json, ProfileJsonOptions.Default);
            if (loaded is not null)
            {
                overrides = new Dictionary<string, DeviceCategory>(loaded, StringComparer.Ordinal);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to load device category overrides; starting empty.");
            overrides = new(StringComparer.Ordinal);
        }
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(overrides, ProfileJsonOptions.Default);
            File.WriteAllText(AppPaths.DeviceCategoryOverridesFile, json);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to persist device category overrides.");
        }
    }
}
