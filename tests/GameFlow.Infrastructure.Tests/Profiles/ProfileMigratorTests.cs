using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Profiles;
using GameFlow.Infrastructure.Tests.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Profiles;

/// <summary>
/// <see cref="ProfileMigrator"/> is the single upgrade path for every way a
/// profile enters the app. These pin that it is idempotent, that it keys on
/// the data rather than the version number, and that each load path really
/// goes through it.
/// </summary>
public sealed class ProfileMigratorTests : IDisposable
{
    private static readonly AxisInvert LegacyInvert = new() { RightY = true, LeftX = false };

    private readonly string directory = Path.Combine(Path.GetTempPath(), "gameflow-migrator-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ProfileDocument Legacy(string id) =>
        new() { Id = id, Name = id, Version = 6, Invert = LegacyInvert };

    [Fact]
    public void MigratingTwice_ChangesNothingTheSecondTime()
    {
        var once = ProfileMigrator.Migrate(Legacy("racing"));

        Assert.Same(once, ProfileMigrator.Migrate(once));
        Assert.Single(once.Rules.OfType<AxisInvertRule>());
    }

    [Fact]
    public void LegacyDataIsConverted_EvenWhenTheVersionAlreadySaysCurrent()
    {
        // Keyed on the data: a file hand-edited (or written by an older
        // build after the bump) must not keep a header the pipeline ignores.
        var migrated = ProfileMigrator.Migrate(Legacy("racing") with { Version = ProfileMigrator.CurrentVersion });

        Assert.Single(migrated.Rules.OfType<AxisInvertRule>());
        Assert.Null(migrated.Invert);
    }

    [Fact]
    public void AHeaderWithNoAxisOpinion_IsClearedWithoutAddingARule()
    {
        var migrated = ProfileMigrator.Migrate(new ProfileDocument { Id = "p", Invert = new AxisInvert { ShowInLayout = true } });

        Assert.Empty(migrated.Rules);
        Assert.Null(migrated.Invert);
    }

    [Fact]
    public void AHeaderThatOnlySwitchesAnAxisOff_StillBecomesARule()
    {
        // Inverts nothing on its own, but over a lower profile in a slot it
        // is what keeps that profile's inversion off.
        var migrated = ProfileMigrator.Migrate(new ProfileDocument { Id = "p", Invert = new AxisInvert { RightY = false } });

        Assert.False(Assert.IsType<AxisInvertRule>(Assert.Single(migrated.Rules)).Axes.RightY);
    }

    [Fact]
    public void AHeaderWrittenAfterConversion_FoldsIntoTheExistingRule()
    {
        var converted = ProfileMigrator.Migrate(Legacy("racing"));

        // An older build ticks RightY back off through the header.
        var migrated = ProfileMigrator.Migrate(converted with { Invert = new AxisInvert { RightY = false } });

        var rule = Assert.Single(migrated.Rules.OfType<AxisInvertRule>());
        Assert.False(rule.Axes.RightY);
        Assert.False(rule.Axes.LeftX);
    }

    [Fact]
    public async Task JsonRepository_MigratesOnLoad_ButLoadStoredReturnsTheFileAsWritten()
    {
        var repository = new JsonProfileRepository(NullLogger<JsonProfileRepository>.Instance, directory);
        _ = Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "racing.json"),
            """{ "id": "racing", "name": "Racing", "version": 6, "rules": [], "invert": { "rightY": true } }""",
            TestContext.Current.CancellationToken);

        var loaded = await repository.LoadAsync("racing", TestContext.Current.CancellationToken);
        var stored = await repository.LoadStoredAsync("racing", TestContext.Current.CancellationToken);

        Assert.Equal("axis-invert-racing", Assert.IsType<AxisInvertRule>(Assert.Single(loaded!.Rules)).Id);
        Assert.Null(loaded.Invert);
        Assert.NotNull(stored!.Invert);
        Assert.Empty(stored.Rules);
    }

    [Fact]
    public async Task StartUp_RewritesALegacyActiveProfileInTheCurrentShape()
    {
        var repository = new JsonProfileRepository(NullLogger<JsonProfileRepository>.Instance, directory);
        _ = Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "racing.json"),
            """{ "id": "racing", "name": "Racing", "version": 6, "rules": [], "invert": { "rightY": true } }""",
            TestContext.Current.CancellationToken);
        var memory = new InMemoryProfileRepository();
        await memory.SaveSettingsAsync(new AppSettings { ActiveProfileId = "racing" }, TestContext.Current.CancellationToken);
        var settings = new UserSettingsService(memory, new NoLogLevelSwitch(), NullLogger<UserSettingsService>.Instance);
        var session = new ProfileSession(new SettingsFromMemoryProfilesFromDisk(memory, repository), settings, NullLogger<ProfileSession>.Instance);

        await session.EnsureInitializedAsync(TestContext.Current.CancellationToken);

        Assert.Single(session.CurrentProfile.Rules.OfType<AxisInvertRule>());
        var onDisk = await File.ReadAllTextAsync(Path.Combine(directory, "racing.json"), TestContext.Current.CancellationToken);
        Assert.Contains("axis-invert-racing", onDisk, StringComparison.Ordinal);
        Assert.DoesNotContain("\"invert\"", onDisk, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Import_ConvertsTheHeaderUnderTheNewProfileId()
    {
        var memory = new InMemoryProfileRepository();
        var settings = new UserSettingsService(memory, new NoLogLevelSwitch(), NullLogger<UserSettingsService>.Instance);
        var session = new ProfileSession(memory, settings, NullLogger<ProfileSession>.Instance);
        await session.EnsureInitializedAsync(TestContext.Current.CancellationToken);

        await session.ImportProfileAsync(
            """{ "id": "exported", "name": "Exported", "version": 6, "rules": [], "invert": { "leftY": true } }""",
            TestContext.Current.CancellationToken);

        var rule = Assert.IsType<AxisInvertRule>(Assert.Single(session.CurrentProfile.Rules));
        Assert.Equal(ProfileMigrator.AxisInvertRuleId(session.CurrentProfile.Id), rule.Id);
        Assert.NotEqual("axis-invert-exported", rule.Id);
        Assert.True(rule.Axes.LeftY);
        Assert.Null(session.CurrentProfile.Invert);
    }

    [Fact]
    public async Task Switch_ConvertsTheHeader_EvenFromARepositoryThatDoesNotMigrate()
    {
        var memory = new InMemoryProfileRepository();
        memory.Add(Legacy("racing"));
        var settings = new UserSettingsService(memory, new NoLogLevelSwitch(), NullLogger<UserSettingsService>.Instance);
        var session = new ProfileSession(memory, settings, NullLogger<ProfileSession>.Instance);
        await session.EnsureInitializedAsync(TestContext.Current.CancellationToken);

        await session.SwitchToProfileAsync("racing", TestContext.Current.CancellationToken);

        Assert.Single(session.CurrentProfile.Rules.OfType<AxisInvertRule>());
        Assert.Null(session.CurrentProfile.Invert);
    }

    [Fact]
    public async Task SavingAProfileThatStillCarriesTheHeader_StoresTheRuleInstead()
    {
        var memory = new InMemoryProfileRepository();
        var settings = new UserSettingsService(memory, new NoLogLevelSwitch(), NullLogger<UserSettingsService>.Instance);
        var session = new ProfileSession(memory, settings, NullLogger<ProfileSession>.Instance);
        await session.EnsureInitializedAsync(TestContext.Current.CancellationToken);

        await session.SaveCurrentProfileAsync(session.CurrentProfile with { Invert = new AxisInvert { RightX = true } }, TestContext.Current.CancellationToken);

        var saved = await memory.LoadAsync(session.CurrentProfile.Id, TestContext.Current.CancellationToken);
        Assert.Null(saved!.Invert);
        Assert.True(Assert.Single(saved.Rules.OfType<AxisInvertRule>()).Axes.RightX);
    }

    /// <summary>Settings in memory, profiles on disk: lets a start-up test read back the real file.</summary>
    private sealed class SettingsFromMemoryProfilesFromDisk(InMemoryProfileRepository settings, JsonProfileRepository profiles) : IProfileRepository
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default) => profiles.ListAsync(cancellationToken);
        public Task<ProfileDocument?> LoadAsync(string profileId, CancellationToken cancellationToken = default) => profiles.LoadAsync(profileId, cancellationToken);
        public Task<ProfileDocument?> LoadStoredAsync(string profileId, CancellationToken cancellationToken = default) => profiles.LoadStoredAsync(profileId, cancellationToken);
        public Task SaveAsync(ProfileDocument profile, CancellationToken cancellationToken = default) => profiles.SaveAsync(profile, cancellationToken);
        public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken = default) => settings.LoadSettingsAsync(cancellationToken);
        public Task SaveSettingsAsync(AppSettings appSettings, CancellationToken cancellationToken = default) => settings.SaveSettingsAsync(appSettings, cancellationToken);
        public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default) => profiles.DeleteAsync(profileId, cancellationToken);
        public string GetProfilePath(string profileId) => profiles.GetProfilePath(profileId);
    }
}
