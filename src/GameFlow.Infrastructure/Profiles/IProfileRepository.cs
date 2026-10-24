using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Profiles;

public interface IProfileRepository
{
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// The profile in the current shape — see <see cref="ProfileMigrator"/>.
    /// What every reader should use.
    /// </summary>
    Task<ProfileDocument?> LoadAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The profile exactly as stored, before <see cref="ProfileMigrator"/>
    /// runs. Only start-up uses it, to tell whether the file is in an older
    /// shape and should be rewritten; a repository that does not migrate on
    /// read has nothing to tell apart, hence the default.
    /// </summary>
    Task<ProfileDocument?> LoadStoredAsync(string profileId, CancellationToken cancellationToken = default) =>
        LoadAsync(profileId, cancellationToken);

    Task SaveAsync(ProfileDocument profile, CancellationToken cancellationToken = default);

    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default);

    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);

    Task DeleteAsync(string profileId, CancellationToken cancellationToken = default);
    string GetProfilePath(string profileId);
}
