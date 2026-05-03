using System.Text.Json;
using Autofire.Core.Models;
using Autofire.Infrastructure.Configuration;

namespace Autofire.Infrastructure.Profiles;

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

    public async Task SaveAsync(ProfileDocument profile, CancellationToken cancellationToken = default)
    {
        var path = GetProfilePath(profile.Id);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, profile, ProfileJsonOptions.Default, cancellationToken);
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

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.SettingsFile)!);

        await using var stream = File.Create(AppPaths.SettingsFile);
        await JsonSerializer.SerializeAsync(stream, settings, ProfileJsonOptions.Default, cancellationToken);
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
