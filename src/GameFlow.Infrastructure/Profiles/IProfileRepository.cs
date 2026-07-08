using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Profiles;

public interface IProfileRepository
{
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    Task<ProfileDocument?> LoadAsync(string profileId, CancellationToken cancellationToken = default);

    Task SaveAsync(ProfileDocument profile, CancellationToken cancellationToken = default);

    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task DeleteAsync(string profileId, CancellationToken cancellationToken = default);
    string GetProfilePath(string profileId);
}
