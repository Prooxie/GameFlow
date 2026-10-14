using System.Text.Json;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Configuration;

namespace GameFlow.Infrastructure.Profiles;

public sealed class JsonProfileRepository : IProfileRepository
{
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        _ = Directory.CreateDirectory(AppPaths.ProfilesDirectory);

        return await Task.Run<IReadOnlyList<string>>(
            () => [.. Directory
                .EnumerateFiles(AppPaths.ProfilesDirectory, "*.json", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileNameWithoutExtension)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Cast<string>()],
            cancellationToken);
    }

    public async Task<ProfileDocument?> LoadAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var path = GetProfilePath(profileId);
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ProfileDocument>(stream, ProfileJsonOptions.Default, cancellationToken);
    }

    public Task SaveAsync(ProfileDocument profile, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(profile, ProfileJsonOptions.Default);
        return AtomicFile.WriteAllTextAsync(GetProfilePath(profile.Id), json, cancellationToken);
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(AppPaths.SettingsFile))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(AppPaths.SettingsFile);
        var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, ProfileJsonOptions.Default, cancellationToken);
        return settings ?? new AppSettings();
    }

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(settings, ProfileJsonOptions.Default);
        return AtomicFile.WriteAllTextAsync(AppPaths.SettingsFile, json, cancellationToken);
    }

    public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var path = GetProfilePath(profileId);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    public string GetProfilePath(string profileId)
    {
        return AppPaths.GetProfileFile(profileId);
    }
}
