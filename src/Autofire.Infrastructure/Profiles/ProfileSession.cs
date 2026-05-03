using System.Text.Json;
using Autofire.Core.Models;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Profiles;

public sealed class ProfileSession(IProfileRepository repository, ILogger<ProfileSession> logger)
{
    private readonly IProfileRepository repository = repository;
    private readonly ILogger<ProfileSession> logger = logger;
    private readonly SemaphoreSlim gate = new(1, 1);
    private bool isInitialized;

    public event EventHandler? Changed;

    public ProfileDocument CurrentProfile { get; private set; } = ProfileDefaults.CreateSpeedrunnerDefault();

    public AppSettings Settings { get; private set; } = new();

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

            Settings = await repository.LoadSettingsAsync(cancellationToken);

            var current = await repository.LoadAsync(Settings.ActiveProfileId, cancellationToken);
            if (current is null)
            {
                current = ProfileDefaults.CreateSpeedrunnerDefault();
                await repository.SaveAsync(current, cancellationToken);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Created default profile at {ProfilePath}.", repository.GetProfilePath(current.Id));
                }
            }
            else
            {
                var migrated = MigrateProfile(current);
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
        var profile = JsonSerializer.Deserialize<ProfileDocument>(json, ProfileJsonOptions.Default)
            ?? throw new InvalidOperationException("Profile JSON could not be parsed.");

        await SaveCurrentProfileAsync(profile, cancellationToken);
    }

    public async Task SaveCurrentProfileAsync(ProfileDocument profile, CancellationToken cancellationToken = default)
    {
        CurrentProfile = profile;
        await repository.SaveAsync(profile, cancellationToken);

        Settings = Settings with { ActiveProfileId = profile.Id };
        await repository.SaveSettingsAsync(Settings, cancellationToken);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ResetToDefaultAsync(CancellationToken cancellationToken = default)
    {
        CurrentProfile = ProfileDefaults.CreateSpeedrunnerDefault();
        await repository.SaveAsync(CurrentProfile, cancellationToken);

        Settings = Settings with { ActiveProfileId = CurrentProfile.Id };
        await repository.SaveSettingsAsync(Settings, cancellationToken);

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetCultureAsync(string cultureCode, CancellationToken cancellationToken = default)
    {
        Settings = Settings with { SelectedCulture = cultureCode };
        await repository.SaveSettingsAsync(Settings, cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

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

    public async Task SwitchToProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            var profile = await repository.LoadAsync(profileId, cancellationToken);
            if (profile is null)
            {
                return;
            }

            CurrentProfile = MigrateProfile(profile);
            Settings = Settings with { ActiveProfileId = profileId };
            await repository.SaveSettingsAsync(Settings, cancellationToken);
        }
        finally
        {
            _ = gate.Release();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<ProfileDocument> CreateNewProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        var id = $"profile-{Guid.NewGuid():N}";
        var profile = ProfileDefaults.CreateSpeedrunnerDefault() with { Id = id, Name = name };
        await SaveCurrentProfileAsync(profile, cancellationToken);
        return profile;
    }

    public async Task<ProfileDocument> DuplicateCurrentProfileAsync(string newName, CancellationToken cancellationToken = default)
    {
        var id = $"profile-{Guid.NewGuid():N}";
        var profile = CurrentProfile with { Id = id, Name = newName };
        await repository.SaveAsync(profile, cancellationToken);
        await SaveCurrentProfileAsync(profile, cancellationToken);
        return profile;
    }

    public async Task DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        if (string.Equals(profileId, CurrentProfile.Id, StringComparison.Ordinal))
        {
            return;  // Never delete the active profile
        }

        await repository.DeleteAsync(profileId, cancellationToken);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ImportProfileAsync(string json, CancellationToken cancellationToken = default)
    {
        var profile = JsonSerializer.Deserialize<ProfileDocument>(json, ProfileJsonOptions.Default)
            ?? throw new InvalidOperationException("Could not parse the profile JSON.");
        var id = $"profile-{Guid.NewGuid():N}";
        profile = profile with { Id = id };
        await SaveCurrentProfileAsync(profile, cancellationToken);
    }

    private static ProfileDocument MigrateProfile(ProfileDocument profile)
    {
        var inputProvider = NormalizeInputProvider(profile.InputProvider);
        var preferredInputDeviceId = profile.PreferredInputDeviceId?.Trim() ?? string.Empty;
        var version = Math.Max(profile.Version, 4);

        return version == profile.Version &&
            string.Equals(inputProvider, profile.InputProvider, StringComparison.Ordinal) &&
            string.Equals(preferredInputDeviceId, profile.PreferredInputDeviceId, StringComparison.Ordinal)
            ? profile
            : (profile with
            {
                Version = version,
                InputProvider = inputProvider,
                PreferredInputDeviceId = preferredInputDeviceId
            });
    }

    private static string NormalizeInputProvider(string? inputProvider)
    {
        return string.IsNullOrWhiteSpace(inputProvider)
            ? "xinput"
            : inputProvider.Trim().ToLowerInvariant() switch
            {
                "sdlgamepad" => "sdl",
                "sdl3" => "sdl",
                "sdl-unified" => "sdl",
                "sdl-unified-gamepad" => "sdl",
                "gameinput" => "xinput",   // GameInput was experimental — map to xinput
                _ => inputProvider.Trim().ToLowerInvariant()
            };
    }
}
