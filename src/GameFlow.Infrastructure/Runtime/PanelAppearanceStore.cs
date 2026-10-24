using System.Text.Json;
using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Profiles;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime;

/// <summary>
/// Background colour of each dashboard controller surface, persisted to
/// <see cref="AppPaths.PanelAppearanceFile"/> and keyed by surface — see
/// <see cref="PhysicalKey"/> and <see cref="VirtualKey"/>.
///
/// <para>
/// Deliberately NOT a field on the slot. Every slot change raises
/// <c>SlotsChanged</c>, and the runtime answers that by rebuilding every
/// slot pipeline — disposing and re-creating each virtual controller. A
/// colour picked for a stream would have disconnected the pads mid-game.
/// It is not part of the profile either: profiles switch per game, and a
/// chroma-key background belongs to the stream layout, not to the game.
/// </para>
///
/// <para>
/// A surface with no entry follows the dashboard's global default, which
/// remains on the profile's UI preferences.
/// </para>
/// </summary>
public sealed class PanelAppearanceStore
{
    /// <summary>Keeps a damaged or hand-edited file from growing without bound.</summary>
    private const int MaxEntries = 512;

    private readonly ILogger<PanelAppearanceStore> logger;
    private readonly string filePath;
    private readonly Lock gate = new();
    private Dictionary<string, string> backgrounds = new(StringComparer.Ordinal);

    public PanelAppearanceStore(ILogger<PanelAppearanceStore> logger, string? overridePath = null)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        filePath = overridePath ?? AppPaths.PanelAppearanceFile;
        Load();
    }

    /// <summary>Raised after a surface's background changes (key payload).</summary>
    public event EventHandler<string>? Changed;

    /// <summary>Key of a dashboard panel's physical (input) surface.</summary>
    public static string PhysicalKey(string panelId) => panelId + ":physical";

    /// <summary>Key of a dashboard panel's virtual (output) surface.</summary>
    public static string VirtualKey(string panelId) => panelId + ":virtual";

    /// <summary>The surface's own background, or null when it follows the global default.</summary>
    public string? GetBackground(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        lock (gate)
        {
            return backgrounds.TryGetValue(key, out var value) ? value : null;
        }
    }

    /// <summary>
    /// Sets a surface's background. Null clears it, so the surface follows
    /// the global default again; an empty string is an explicit "theme
    /// default" that does not follow a global chroma colour.
    /// </summary>
    public void SetBackground(string key, string? brush)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        var normalized = brush?.Trim();
        lock (gate)
        {
            var existing = backgrounds.TryGetValue(key, out var current) ? current : null;
            if (string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (normalized is null)
            {
                backgrounds.Remove(key);
            }
            else if (backgrounds.Count < MaxEntries || backgrounds.ContainsKey(key))
            {
                backgrounds[key] = normalized;
            }
            else
            {
                logger.LogWarning("Panel background for {Key} not saved: {Max} surfaces already have one.", key, MaxEntries);
                return;
            }

            Persist();
        }

        Changed?.Invoke(this, key);
    }

    /// <summary>Forgets both surfaces of a panel, e.g. when its slot is deleted.</summary>
    public void RemovePanel(string panelId)
    {
        if (string.IsNullOrWhiteSpace(panelId))
        {
            return;
        }

        bool removed;
        lock (gate)
        {
            removed = backgrounds.Remove(PhysicalKey(panelId)) | backgrounds.Remove(VirtualKey(panelId));
            if (removed)
            {
                Persist();
            }
        }

        if (removed)
        {
            Changed?.Invoke(this, panelId);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(
                File.ReadAllText(filePath), ProfileJsonOptions.Default);
            if (loaded is not null)
            {
                backgrounds = loaded
                    .Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value is not null)
                    .Take(MaxEntries)
                    .ToDictionary(pair => pair.Key, pair => pair.Value.Trim(), StringComparer.Ordinal);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to load panel backgrounds; every panel follows the default.");
            backgrounds = new(StringComparer.Ordinal);
        }
    }

    private void Persist()
    {
        try
        {
            AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(backgrounds, ProfileJsonOptions.Default));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to save panel backgrounds.");
        }
    }
}
