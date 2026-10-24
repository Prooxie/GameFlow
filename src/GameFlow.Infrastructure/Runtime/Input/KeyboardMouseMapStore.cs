using System.Collections.Frozen;
using System.Text.Json;
using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Profiles;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Input;

/// <summary>
/// Persists the <see cref="KeyboardMouseMap"/> to
/// <see cref="AppPaths.KeyboardMouseMapFile"/> and hands the input source
/// its compiled form.
///
/// <para>
/// <see cref="Compiled"/> is read on every keyboard and mouse read — up to
/// a thousand times a second — so it is a published immutable object
/// swapped on save, never rebuilt per read and never locked for.
/// </para>
/// </summary>
public sealed class KeyboardMouseMapStore
{
    private readonly ILogger<KeyboardMouseMapStore> logger;
    private readonly string filePath;
    private readonly Lock writeGate = new();
    private volatile CompiledKeyboardMouseMap compiled = CompiledKeyboardMouseMap.Default;
    private volatile FrozenDictionary<string, CompiledKeyboardMouseMap> perDevice =
        new Dictionary<string, CompiledKeyboardMouseMap>(StringComparer.Ordinal).ToFrozenDictionary(StringComparer.Ordinal);

    private sealed record MapFile
    {
        public int Version { get; init; } = 2;
        public KeyboardMouseMap Defaults { get; init; } = KeyboardMouseMap.Default;
        public Dictionary<string, KeyboardMouseMap> Devices { get; init; } = new(StringComparer.Ordinal);
    }

    public KeyboardMouseMapStore(ILogger<KeyboardMouseMapStore> logger, string? overridePath = null)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        filePath = overridePath ?? AppPaths.KeyboardMouseMapFile;
        Load();
    }

    /// <summary>Raised after the map changes, on the thread that saved it.</summary>
    public event EventHandler? Changed;

    /// <summary>The current map, normalized.</summary>
    public KeyboardMouseMap Current => compiled.Source;

    /// <summary>The current map in the form the synthesizers read.</summary>
    public CompiledKeyboardMouseMap Compiled => compiled;

    public CompiledKeyboardMouseMap CompiledFor(string? deviceId) =>
        deviceId is not null && perDevice.TryGetValue(deviceId, out var map) ? map : compiled;

    public KeyboardMouseMap CurrentFor(string? deviceId) => CompiledFor(deviceId).Source;

    public void SaveFor(KeyboardMouseMap map, string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) { Save(map); return; }
        var next = map.Compile();
        lock (writeGate)
        {
            if (perDevice.TryGetValue(deviceId, out var previous) && SameMap(previous.Source, next.Source)) { return; }
            if (deviceId.Length > 4096 || (!perDevice.ContainsKey(deviceId) && perDevice.Count >= 128))
            { throw new InvalidOperationException("The per-device input map limit was reached."); }
            var updated = perDevice.ToDictionary(StringComparer.Ordinal);
            updated[deviceId] = next;
            perDevice = updated.ToFrozenDictionary(StringComparer.Ordinal);
            Persist(compiled.Source);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void ResetFor(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) { ResetToDefault(); return; }
        lock (writeGate)
        {
            var updated = perDevice.ToDictionary(StringComparer.Ordinal);
            if (!updated.Remove(deviceId)) { return; }
            perDevice = updated.ToFrozenDictionary(StringComparer.Ordinal);
            Persist(compiled.Source);
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Normalizes, publishes and persists <paramref name="map"/>.</summary>
    public void Save(KeyboardMouseMap map)
    {
        ArgumentNullException.ThrowIfNull(map);

        var next = map.Compile();
        lock (writeGate)
        {
            if (SameMap(next.Source, compiled.Source))
            {
                return;
            }

            compiled = next;
            Persist(next.Source);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Restores the built-in layout.</summary>
    public void ResetToDefault() => Save(KeyboardMouseMap.Default);

    /// <summary>
    /// Value comparison. Record equality alone compares the binding list by
    /// reference, which would rewrite the file on every no-op save.
    /// </summary>
    private static bool SameMap(KeyboardMouseMap a, KeyboardMouseMap b) =>
        a.MouseMotion == b.MouseMotion
        && a.MouseSensitivity.Equals(b.MouseSensitivity)
        && a.MouseAntiDeadzone.Equals(b.MouseAntiDeadzone)
        && a.InvertMouseY == b.InvertMouseY
        && a.Bindings.SequenceEqual(b.Bindings);

    private void Load()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            if (new FileInfo(filePath).Length > 4 * 1024 * 1024) { throw new IOException("Input maps exceed the file size limit."); }
            var json = File.ReadAllText(filePath);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("defaults", out _) || document.RootElement.TryGetProperty("Defaults", out _))
            {
                var file = JsonSerializer.Deserialize<MapFile>(json, ProfileJsonOptions.Default)!;
                if (file.Version != 2) { throw new IOException("Unsupported input map version."); }
                var defaults = file.Defaults.Compile();
                var devices = file.Devices.Where(pair => pair.Key.Length is > 0 and <= 4096 && pair.Value is not null)
                    .Take(128).ToDictionary(pair => pair.Key, pair => pair.Value.Compile(), StringComparer.Ordinal);
                compiled = defaults;
                perDevice = devices.ToFrozenDictionary(StringComparer.Ordinal);
                return;
            }
            var loaded = JsonSerializer.Deserialize<KeyboardMouseMap>(json, ProfileJsonOptions.Default);
            if (loaded is not null)
            {
                compiled = loaded.Compile();
            }
        }
        catch (Exception exception)
        {
            // A damaged file must not leave the keyboard dead: fall back to
            // the defaults, which is what a first run uses anyway.
            logger.LogWarning(exception, "Failed to load the keyboard & mouse map; using the default layout.");
            compiled = CompiledKeyboardMouseMap.Default;
        }
    }

    private void Persist(KeyboardMouseMap map)
    {
        try
        {
            var file = new MapFile
            {
                Defaults = map,
                Devices = perDevice.ToDictionary(pair => pair.Key, pair => pair.Value.Source, StringComparer.Ordinal)
            };
            AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(file, ProfileJsonOptions.Default));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to save the keyboard & mouse map.");
        }
    }
}
