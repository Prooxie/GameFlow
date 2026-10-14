using System.Text.Json;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Profiles;

/// <summary>
/// Stores each profile as <c>{id}.json</c> in the profiles folder, and the app
/// settings in <see cref="AppPaths.SettingsFile"/>. All writes are atomic, see
/// <see cref="AtomicFile"/>.
/// </summary>
/// <remarks>
/// A profile file that does not parse is moved to <c>{id}.json.damaged</c> and
/// reads as missing. The caller then uses the default profile, the file list
/// no longer shows the damaged file, and the user can repair the copy by hand.
/// Before this, one damaged file stopped the start-up and the profile list.
/// </remarks>
/// <param name="logger">Logs a damaged file.</param>
/// <param name="profilesDirectory">
/// Profiles folder for tests. Null uses <see cref="AppPaths.ProfilesDirectory"/>,
/// which follows the folder chosen in the settings.
/// </param>
public sealed class JsonProfileRepository(
    ILogger<JsonProfileRepository> logger,
    string? profilesDirectory = null) : IProfileRepository
{
    /// <summary>Suffix of a profile file that did not parse.</summary>
    private const string DamagedSuffix = ".damaged";

    private readonly ILogger<JsonProfileRepository> logger = logger;
    private readonly string? profilesDirectory = profilesDirectory;

    private string ProfilesDirectory => profilesDirectory ?? AppPaths.ProfilesDirectory;

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        var folder = ProfilesDirectory;
        _ = Directory.CreateDirectory(folder);

        return await Task.Run<IReadOnlyList<string>>(
            () => [.. Directory
                .EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)
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

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ProfileDocument>(stream, ProfileJsonOptions.Default, cancellationToken);
        }
        catch (JsonException exception)
        {
            MoveAside(profileId, path, exception);
            return null;
        }
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
        return Path.Combine(ProfilesDirectory, $"{profileId}.json");
    }

    /// <summary>
    /// Moves a profile file that does not parse to <c>{path}.damaged</c>. An older
    /// damaged copy is replaced, because the newest one is the one to repair.
    /// </summary>
    private void MoveAside(string profileId, string path, JsonException exception)
    {
        var damagedPath = path + DamagedSuffix;
        try
        {
            File.Move(path, damagedPath, overwrite: true);
            logger.LogWarning(
                exception,
                "Profile {ProfileId} could not be read and was moved to {DamagedPath}. GameFlow uses the default profile.",
                profileId,
                damagedPath);
        }
        catch (Exception moveException) when (moveException is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                exception,
                "Profile {ProfileId} at {Path} could not be read, and it could not be moved aside ({Reason}).",
                profileId,
                path,
                moveException.Message);
        }
    }
}
