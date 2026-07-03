using System.Text.Json;
using Autofire.Infrastructure.Configuration;
using Autofire.Infrastructure.Profiles;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime.Input;

/// <summary>
/// Persists per-device <see cref="DeviceButtonMap"/>s to
/// <see cref="AppPaths.ButtonMapsFile"/> as a JSON map keyed by device id.
/// Loaded once on construction; <see cref="Save"/> upserts and rewrites.
/// The SDL input source reads maps from here to remap recognized buttons.
/// </summary>
public sealed class ButtonMapStore
{
    private readonly ILogger<ButtonMapStore> logger;
    private readonly Lock gate = new();
    private Dictionary<string, DeviceButtonMap> maps = new(StringComparer.Ordinal);

    public ButtonMapStore(ILogger<ButtonMapStore> logger)
    {
        this.logger = logger;
        Load();
    }

    /// <summary>Raised after a map is saved or cleared (device id payload).</summary>
    public event EventHandler<string>? MapChanged;

    /// <summary>Returns a detached copy of the device's map, or null if none.</summary>
    public DeviceButtonMap? GetOrNull(string deviceId)
    {
        lock (gate)
        {
            return maps.TryGetValue(deviceId, out var existing) ? existing.Clone() : null;
        }
    }

    public bool Has(string deviceId)
    {
        lock (gate)
        {
            return maps.ContainsKey(deviceId);
        }
    }

    /// <summary>Upserts the map and persists.</summary>
    public void Save(DeviceButtonMap map)
    {
        if (map is null || string.IsNullOrEmpty(map.DeviceId))
        {
            return;
        }
        lock (gate)
        {
            maps[map.DeviceId] = map.Clone();
            Persist();
        }
        MapChanged?.Invoke(this, map.DeviceId);
    }

    /// <summary>Removes the device's map and persists.</summary>
    public void Remove(string deviceId)
    {
        bool removed;
        lock (gate)
        {
            removed = maps.Remove(deviceId);
            if (removed)
            {
                Persist();
            }
        }
        if (removed)
        {
            MapChanged?.Invoke(this, deviceId);
        }
    }

    private void Load()
    {
        try
        {
            var path = AppPaths.ButtonMapsFile;
            if (!File.Exists(path))
            {
                return;
            }
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, DeviceButtonMap>>(json, ProfileJsonOptions.Default);
            if (loaded is not null)
            {
                maps = new Dictionary<string, DeviceButtonMap>(loaded, StringComparer.Ordinal);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to load button maps; starting empty.");
            maps = new(StringComparer.Ordinal);
        }
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(maps, ProfileJsonOptions.Default);
            File.WriteAllText(AppPaths.ButtonMapsFile, json);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to persist button maps.");
        }
    }
}
