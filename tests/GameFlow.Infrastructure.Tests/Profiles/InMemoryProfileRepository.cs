using GameFlow.Core.Models;
using GameFlow.Infrastructure.Profiles;

namespace GameFlow.Infrastructure.Tests.Profiles;

/// <summary>
/// In-memory <see cref="IProfileRepository"/> for tests. It keeps the last saved
/// settings, so a test can check what reached "disk".
/// </summary>
internal sealed class InMemoryProfileRepository : IProfileRepository
{
    private readonly Dictionary<string, ProfileDocument> profiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The settings as the last save wrote them.</summary>
    public AppSettings SavedSettings { get; private set; } = new();

    /// <summary>When set, <see cref="LoadSettingsAsync"/> throws it, as for a damaged file.</summary>
    public Exception? LoadSettingsFailure { get; init; }

    public void Add(ProfileDocument profile) => profiles[profile.Id] = profile;

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>([.. profiles.Keys]);

    public Task<ProfileDocument?> LoadAsync(string profileId, CancellationToken cancellationToken = default) =>
        Task.FromResult(profiles.GetValueOrDefault(profileId));

    public Task SaveAsync(ProfileDocument profile, CancellationToken cancellationToken = default)
    {
        profiles[profile.Id] = profile;
        return Task.CompletedTask;
    }

    public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) =>
        LoadSettingsFailure is null ? Task.FromResult(SavedSettings) : Task.FromException<AppSettings>(LoadSettingsFailure);

    public async Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        // A real write takes time. The yield lets two concurrent updates overlap here.
        await Task.Yield();
        SavedSettings = settings;
    }

    public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
    {
        _ = profiles.Remove(profileId);
        return Task.CompletedTask;
    }

    public string GetProfilePath(string profileId) => profileId + ".json";
}
