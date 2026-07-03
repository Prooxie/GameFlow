using System.Text.Json;
using Autofire.Infrastructure.Configuration;
using Autofire.Infrastructure.Profiles;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime.Templates;

/// <summary>
/// Persists per-device <see cref="DeviceOutputTemplate"/>s to
/// <see cref="AppPaths.DeviceTemplatesFile"/> as a JSON map keyed by
/// device id. Loaded once on construction; each <see cref="Save"/>
/// upserts and rewrites the file. The HidMaestro output sink reads
/// templates from here to decide what virtual device to emit for each
/// physical device (Phase 2b).
/// </summary>
public sealed class DeviceTemplateStore
{
    private readonly ILogger<DeviceTemplateStore> logger;
    private readonly Lock gate = new();
    private Dictionary<string, DeviceOutputTemplate> templates = new(StringComparer.Ordinal);

    public DeviceTemplateStore(ILogger<DeviceTemplateStore> logger)
    {
        this.logger = logger;
        Load();
    }

    /// <summary>Raised after a template is saved, so listeners (e.g. the output sink) can react.</summary>
    public event EventHandler<string>? TemplateChanged;

    /// <summary>
    /// Returns a detached copy of the template for <paramref name="deviceId"/>,
    /// creating a default (disabled) one if none exists yet. Callers edit the
    /// copy and pass it to <see cref="Save"/>.
    /// </summary>
    public DeviceOutputTemplate GetOrCreate(string deviceId)
    {
        lock (gate)
        {
            if (templates.TryGetValue(deviceId, out var existing))
            {
                return existing.Clone();
            }
        }

        return new DeviceOutputTemplate { DeviceId = deviceId };
    }

    /// <summary>True if a stored template exists for the device.</summary>
    public bool Has(string deviceId)
    {
        lock (gate)
        {
            return templates.ContainsKey(deviceId);
        }
    }

    /// <summary>Upserts the template and persists the store.</summary>
    public void Save(DeviceOutputTemplate template)
    {
        if (template is null || string.IsNullOrWhiteSpace(template.DeviceId))
        {
            return;
        }

        lock (gate)
        {
            templates[template.DeviceId] = template.Clone();
            Persist();
        }

        TemplateChanged?.Invoke(this, template.DeviceId);
    }

    private void Load()
    {
        try
        {
            var path = AppPaths.DeviceTemplatesFile;
            if (!File.Exists(path))
            {
                return;
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, DeviceOutputTemplate>>(
                json, ProfileJsonOptions.Default);
            if (loaded is not null)
            {
                templates = new Dictionary<string, DeviceOutputTemplate>(loaded, StringComparer.Ordinal);
            }
        }
        catch (Exception exception)
        {
            // A hand-edited / corrupt file shouldn't take down the app.
            logger.LogWarning(exception, "Failed to load device templates; starting empty.");
            templates = new Dictionary<string, DeviceOutputTemplate>(StringComparer.Ordinal);
        }
    }

    private void Persist()
    {
        try
        {
            var json = JsonSerializer.Serialize(templates, ProfileJsonOptions.Default);
            File.WriteAllText(AppPaths.DeviceTemplatesFile, json);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to save device templates.");
        }
    }
}
