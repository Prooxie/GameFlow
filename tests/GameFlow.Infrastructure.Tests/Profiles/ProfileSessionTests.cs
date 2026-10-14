using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Profiles;
using GameFlow.Infrastructure.Tests.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Profiles;

/// <summary>
/// Tests for <see cref="ProfileSession"/>. The session and the settings dialog
/// share one settings file, so a write of one must keep the fields of the other.
/// </summary>
public sealed class ProfileSessionTests
{
    private readonly InMemoryProfileRepository repository = new();
    private readonly UserSettingsService settings;
    private readonly ProfileSession session;

    public ProfileSessionTests()
    {
        settings = new UserSettingsService(repository, new NoLogLevelSwitch(), NullLogger<UserSettingsService>.Instance);
        session = new ProfileSession(repository, settings, NullLogger<ProfileSession>.Instance);
    }

    [Fact]
    public async Task SaveCurrentProfileAsyncOtherSettingsAreKeptAsync()
    {
        await session.EnsureInitializedAsync();
        await settings.UpdateAsync(current => current with { MotionServerEnabled = true });

        await session.SaveCurrentProfileAsync(ProfileDefaults.CreateSpeedrunnerDefault() with { Id = "racing" });

        Assert.True(repository.SavedSettings.MotionServerEnabled);
        Assert.Equal("racing", repository.SavedSettings.ActiveProfileId);
    }

    /// <summary>
    /// The settings dialog builds its write from the shared settings. It must see
    /// the profile switch, or its next write puts the old profile back.
    /// </summary>
    [Fact]
    public async Task SwitchToProfileAsyncSharedSettingsSeeNewProfileAsync()
    {
        repository.Add(ProfileDefaults.CreateSpeedrunnerDefault() with { Id = "racing", Name = "Racing" });
        await session.EnsureInitializedAsync();

        await session.SwitchToProfileAsync("racing");

        Assert.Equal("racing", settings.Current.ActiveProfileId);
        Assert.Equal("racing", session.Settings.ActiveProfileId);
    }

    [Fact]
    public async Task SetCultureAsyncOtherSettingsAreKeptAsync()
    {
        await session.EnsureInitializedAsync();
        await settings.UpdateAsync(current => current with { MotionServerEnabled = true });

        await session.SetCultureAsync("cs");

        Assert.True(repository.SavedSettings.MotionServerEnabled);
        Assert.Equal("cs", repository.SavedSettings.SelectedCulture);
    }

    [Fact]
    public async Task UpdateCurrentProfileAsyncSameProfileSavesChangeAsync()
    {
        await session.EnsureInitializedAsync();
        var id = session.CurrentProfile.Id;

        var saved = await session.UpdateCurrentProfileAsync(id, profile => profile with { Name = "Renamed" });

        Assert.True(saved);
        Assert.Equal("Renamed", session.CurrentProfile.Name);
        Assert.Equal("Renamed", (await repository.LoadAsync(id))!.Name);
    }

    /// <summary>
    /// An edit made in profile A, saved after a switch to profile B, must not
    /// overwrite profile B. Before, the mapping rules of A replaced those of B.
    /// </summary>
    [Fact]
    public async Task UpdateCurrentProfileAsyncProfileSwitchedSkipsChangeAsync()
    {
        repository.Add(ProfileDefaults.CreateSpeedrunnerDefault() with { Id = "racing", Name = "Racing" });
        await session.EnsureInitializedAsync();
        var editedId = session.CurrentProfile.Id;
        await session.SwitchToProfileAsync("racing");

        var saved = await session.UpdateCurrentProfileAsync(editedId, profile => profile with { Name = "Overwritten" });

        Assert.False(saved);
        Assert.Equal("Racing", session.CurrentProfile.Name);
        Assert.Equal("Racing", (await repository.LoadAsync("racing"))!.Name);
    }

    [Fact]
    public async Task EnsureInitializedAsyncSavedActiveProfileIsLoadedAsync()
    {
        repository.Add(ProfileDefaults.CreateSpeedrunnerDefault() with { Id = "racing", Name = "Racing" });
        await settings.UpdateAsync(current => current with { ActiveProfileId = "racing" });

        await session.EnsureInitializedAsync();

        Assert.Equal("racing", session.CurrentProfile.Id);
    }
}
