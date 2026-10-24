using System.Text.Json;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Profiles;

/// <summary>
/// The active profile, and the profile operations of the UI: save, switch,
/// create, duplicate, import and delete.
/// </summary>
/// <remarks>
/// The active profile id and the UI culture live in the settings file. The
/// session writes them through <see cref="IUserSettingsService.UpdateAsync"/>,
/// which is the only writer of that file. See <see cref="IUserSettingsService"/>.
/// </remarks>
public sealed class ProfileSession(
    IProfileRepository repository,
    IUserSettingsService userSettings,
    ILogger<ProfileSession> logger)
{
    public const int MaximumProfileBytes = ProfileValidator.MaximumBytes;
    private readonly IProfileRepository repository = repository;
    private readonly IUserSettingsService userSettings = userSettings;
    private readonly ILogger<ProfileSession> logger = logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool isInitialized;

    public event EventHandler? Changed;

    public ProfileDocument CurrentProfile { get; private set; } = ProfileDefaults.CreateSpeedrunnerDefault();

    /// <summary>The shared settings. Read-only here; see <see cref="IUserSettingsService.UpdateAsync"/>.</summary>
    public AppSettings Settings => userSettings.Current;

    public string CurrentProfilePath => repository.GetProfilePath(CurrentProfile.Id);

    public async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (isInitialized)
        {
            return;
        }

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (isInitialized)
            {
                return;
            }

            await userSettings.InitializeAsync(cancellationToken);

            // The STORED shape, not the migrated one: comparing the two is
            // how start-up knows the file is in an older shape and should
            // be rewritten. Every other read takes the migrated profile.
            var current = await repository.LoadStoredAsync(Settings.ActiveProfileId, cancellationToken);
            if (current is null)
            {
                var fallback = ProfileDefaults.CreateSpeedrunnerDefault();
                current = await repository.LoadAsync(fallback.Id, cancellationToken) ?? fallback;
                await repository.SaveAsync(current, cancellationToken);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Created default profile at {ProfilePath}.", repository.GetProfilePath(current.Id));
                }
            }
            else
            {
                var migrated = ProfileMigrator.Migrate(current);
                if (!ReferenceEquals(migrated, current) && migrated != current)
                {
                    current = migrated;
                    await repository.SaveAsync(current, cancellationToken);
                    if (logger.IsEnabled(LogLevel.Information))
                    {
                        logger.LogInformation("Migrated profile {ProfileId} to version {Version}.", current.Id, current.Version);
                    }
                }
            }

            CurrentProfile = current;
            if (!string.Equals(Settings.ActiveProfileId, current.Id, StringComparison.Ordinal))
            {
                await userSettings.UpdateAsync(settings => settings with { ActiveProfileId = current.Id }, cancellationToken);
            }
            isInitialized = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _ = gate.Release();
        }
    }

    public async Task SaveProfileJsonAsync(string json, CancellationToken cancellationToken = default)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaximumProfileBytes) { throw new JsonException("Profile files are limited to 2 MiB."); }
        var profile = JsonSerializer.Deserialize<ProfileDocument>(json, ProfileJsonOptions.Default)
            ?? throw new InvalidOperationException("Profile JSON could not be parsed.");

        await SaveCurrentProfileAsync(profile, cancellationToken);
    }

    /// <summary>
    /// Persists <paramref name="profile"/> as the current profile and updates
    /// the <see cref="Settings"/> active-profile pointer. Raises <see cref="Changed"/>.
    /// </summary>
    public async Task SaveCurrentProfileAsync(ProfileDocument profile, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { await PersistAsCurrentAsync(profile, cancellationToken); }
        finally { _ = gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Applies <paramref name="change"/> to the current profile and saves it, but only
    /// while <paramref name="expectedProfileId"/> is still the current profile.
    /// Raises <see cref="Changed"/> after a save.
    /// </summary>
    /// <remarks>
    /// Use this for an edit that waits before it saves, for example behind a save lock.
    /// A profile switch in that wait would otherwise put the edit into the new
    /// profile. The check and the save hold the lock that
    /// <see cref="SwitchToProfileAsync"/> holds, so no switch can come between them.
    /// </remarks>
    /// <param name="expectedProfileId">Id of the profile that the user edited.</param>
    /// <param name="change">Makes the saved profile from the current one.</param>
    /// <param name="cancellationToken">Cancels the wait and the write.</param>
    /// <returns>True when the change was saved. False when another profile is current now.</returns>
    public async Task<bool> UpdateCurrentProfileAsync(
        string expectedProfileId,
        Func<ProfileDocument, ProfileDocument> change,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        await gate.WaitAsync(cancellationToken);
        try
        {
            if (!string.Equals(CurrentProfile.Id, expectedProfileId, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation(
                    "Dropped an edit of profile {EditedProfileId}: profile {CurrentProfileId} became current before the save.",
                    expectedProfileId,
                    CurrentProfile.Id);
                return false;
            }

            await PersistAsCurrentAsync(change(CurrentProfile), cancellationToken);
        }
        finally
        {
            _ = gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <remarks>
    /// Every save of the current profile funnels through here, so this is
    /// also where it is migrated: whatever the caller built — raw JSON from
    /// the editor, an import, or an edit made from an older in-memory copy
    /// that still carries the v6 header inversion — is written, and becomes
    /// <see cref="CurrentProfile"/>, in the current shape only.
    /// </remarks>
    private async Task PersistAsCurrentAsync(ProfileDocument profile, CancellationToken cancellationToken)
    {
        profile = ProfileMigrator.Migrate(profile);
        ProfileValidator.Validate(profile);
        await repository.SaveAsync(profile, cancellationToken);
        await SetActiveProfileIdAsync(profile.Id, cancellationToken);
        CurrentProfile = profile;

        logger.LogInformation(
            "Saved profile {ProfileId} ({ProfileName}) and set as active.",
            profile.Id,
            profile.Name);
    }

    /// <summary>
    /// Replaces the current profile with a fresh copy of the speedrunner
    /// default and persists it.
    /// </summary>
    public async Task ResetToDefaultAsync(CancellationToken cancellationToken = default)
    {
        await SaveCurrentProfileAsync(ProfileDefaults.CreateSpeedrunnerDefault(), cancellationToken);

        logger.LogInformation("Profile reset to speedrunner default.");

    }

    /// <summary>
    /// Persists the chosen UI culture into the settings file.
    /// </summary>
    public async Task SetCultureAsync(string cultureCode, CancellationToken cancellationToken = default)
    {
        var previous = Settings.SelectedCulture;
        await userSettings.UpdateAsync(settings => settings with { SelectedCulture = cultureCode }, cancellationToken);

        if (!string.Equals(previous, cultureCode, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "Persisted UI culture: {PreviousCulture} -> {NewCulture}.",
                previous,
                cultureCode);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Points the settings at <paramref name="profileId"/> and keeps every other setting.</summary>
    private Task SetActiveProfileIdAsync(string profileId, CancellationToken cancellationToken) =>
        userSettings.UpdateAsync(settings => settings with { ActiveProfileId = profileId }, cancellationToken);

    public string SerializeCurrentProfile()
    {
        return JsonSerializer.Serialize(CurrentProfile, ProfileJsonOptions.Default);
    }

    public async Task<IReadOnlyList<ProfileSummary>> ListProfilesAsync(CancellationToken cancellationToken = default)
    {
        var ids = await repository.ListAsync(cancellationToken);
        var list = new List<ProfileSummary>();
        foreach (var id in ids)
        {
            var p = await repository.LoadAsync(id, cancellationToken);
            if (p is not null)
            {
                list.Add(new ProfileSummary(p.Id, p.Name));
            }
        }
        return list;
    }

    /// <summary>
    /// Loads the profile with the given <paramref name="profileId"/> and
    /// makes it active. Silently no-ops if the profile file is missing.
    /// </summary>
    public async Task SwitchToProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var profile = await repository.LoadAsync(profileId, cancellationToken);
            if (profile is null)
            {
                logger.LogWarning("Switch requested to profile {ProfileId} but no such file exists.", profileId);
                return;
            }

            var previousId = CurrentProfile.Id;
            var migrated = ProfileMigrator.Migrate(profile);
            ProfileValidator.Validate(migrated);
            await SetActiveProfileIdAsync(profileId, cancellationToken);
            CurrentProfile = migrated;

            logger.LogInformation(
                "Switched active profile: {PreviousProfileId} -> {NewProfileId} ({ProfileName}).",
                previousId,
                CurrentProfile.Id,
                CurrentProfile.Name);
        }
        finally
        {
            _ = gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates a new profile from the speedrunner default with the given
    /// <paramref name="name"/>, persists it, and makes it active.
    /// </summary>
    public async Task<ProfileDocument> CreateNewProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        var id = $"profile-{Guid.NewGuid():N}";
        var profile = ProfileDefaults.CreateSpeedrunnerDefault() with { Id = id, Name = name };

        logger.LogInformation("Creating new profile {ProfileId} ({ProfileName}).", id, name);

        await SaveCurrentProfileAsync(profile, cancellationToken);
        return profile;
    }

    /// <summary>
    /// Saves a copy of the currently-active profile under a new id and
    /// <paramref name="newName"/>, then makes the copy active.
    /// </summary>
    /// <remarks>
    /// Rule ids are deliberately kept, not re-issued. Rule toggles refer to
    /// their targets by id, so fresh ids would break every toggle in the
    /// copy; and an equal id across profiles is how a slot's profile stack
    /// knows two rules are the SAME rule — the composer lets the later
    /// profile's copy replace the earlier one instead of running both (see
    /// <c>SlotProfileComposer</c>). That includes the copied
    /// <c>axis-invert-&lt;original id&gt;</c> rule: stacking a profile on its
    /// own duplicate keeps one inversion, the top one.
    /// </remarks>
    public async Task<ProfileDocument> DuplicateCurrentProfileAsync(string newName, CancellationToken cancellationToken = default)
    {
        var id = $"profile-{Guid.NewGuid():N}";
        var sourceId = CurrentProfile.Id;
        var profile = CurrentProfile with { Id = id, Name = newName };
        logger.LogInformation(
            "Duplicated profile {SourceProfileId} -> {NewProfileId} ({NewProfileName}).",
            sourceId,
            id,
            newName);

        await SaveCurrentProfileAsync(profile, cancellationToken);
        return profile;
    }

    /// <summary>
    /// Deletes the profile with the given <paramref name="profileId"/>.
    /// Refuses to delete the currently-active profile (no-ops in that case).
    /// </summary>
    public async Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (string.Equals(profileId, CurrentProfile.Id, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("Refused to delete profile {ProfileId} because it is currently active.", profileId);
                return;
            }
            await repository.DeleteAsync(profileId, cancellationToken);
            logger.LogInformation("Deleted profile {ProfileId}.", profileId);
        }
        finally { _ = gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Parses the supplied profile JSON, assigns it a fresh id, persists
    /// it, and makes it active. Throws on parse failure.
    /// </summary>
    public async Task ImportProfileAsync(string json, CancellationToken cancellationToken = default)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > ProfileValidator.MaximumBytes) { throw new JsonException("Profile files are limited to 2 MiB."); }
        var profile = JsonSerializer.Deserialize<ProfileDocument>(json, ProfileJsonOptions.Default)
            ?? throw new InvalidOperationException("Could not parse the profile JSON.");
        var id = $"profile-{Guid.NewGuid():N}";
        profile = profile with
        {
            Id = id,
            Rules = profile.Rules?.Select(rule => rule is GameFlow.Core.Models.Rules.ControlScriptRule script
                ? script with { Enabled = false } : rule).ToArray()!
        };
        // After the new id, so a legacy header inversion becomes a rule
        // whose id derives from THIS profile, not the one it was exported from.
        profile = ProfileMigrator.Migrate(profile);
        ProfileValidator.Validate(profile);

        logger.LogInformation(
            "Importing profile (assigned new id {NewProfileId}, original name '{ProfileName}').",
            id,
            profile.Name);

        await SaveCurrentProfileAsync(profile, cancellationToken);
    }
}
