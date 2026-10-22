using GameFlow.Infrastructure.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Profiles;

/// <summary>Tests for <see cref="JsonProfileRepository"/>.</summary>
public sealed class JsonProfileRepositoryTests : IDisposable
{
    private const string DamagedJson = "{ \"id\": \"racing\", \"name\": ";

    private readonly string directory = Path.Combine(Path.GetTempPath(), "gameflow-profiles-" + Guid.NewGuid().ToString("N"));
    private readonly JsonProfileRepository repository;

    public JsonProfileRepositoryTests()
    {
        repository = new JsonProfileRepository(NullLogger<JsonProfileRepository>.Instance, directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private void WriteDamagedProfile()
    {
        _ = Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "racing.json"), DamagedJson);
    }

    [Fact]
    public async Task SaveAsyncThenLoadAsyncReturnsSameProfileAsync()
    {
        var profile = ProfileDefaults.CreateSpeedrunnerDefault() with { Id = "racing", Name = "Racing" };

        await repository.SaveAsync(profile, TestContext.Current.CancellationToken);
        var loaded = await repository.LoadAsync("racing", TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal("Racing", loaded.Name);
    }

    /// <summary>
    /// A damaged active profile stopped the start-up and the profile list.
    /// The caller now gets null and uses the default profile.
    /// </summary>
    [Fact]
    public async Task LoadAsyncDamagedFileReturnsNullAsync()
    {
        WriteDamagedProfile();

        var loaded = await repository.LoadAsync("racing", TestContext.Current.CancellationToken);

        Assert.Null(loaded);
    }

    /// <summary>
    /// The user's data must not be lost: the damaged file moves aside for repair,
    /// and the next start does not try it again.
    /// </summary>
    [Fact]
    public async Task LoadAsyncDamagedFileMovesItAsideAsync()
    {
        WriteDamagedProfile();

        _ = await repository.LoadAsync("racing", TestContext.Current.CancellationToken);

        Assert.False(File.Exists(Path.Combine(directory, "racing.json")));
        Assert.Equal(DamagedJson, await File.ReadAllTextAsync(Path.Combine(directory, "racing.json.damaged"), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ListAsyncDamagedFileMovedAsideIsNotListedAsync()
    {
        WriteDamagedProfile();
        _ = await repository.LoadAsync("racing", TestContext.Current.CancellationToken);

        var ids = await repository.ListAsync(TestContext.Current.CancellationToken);

        Assert.Empty(ids);
    }

    [Fact]
    public async Task LoadAsyncMissingFileReturnsNullAsync()
    {
        var loaded = await repository.LoadAsync("not-here", TestContext.Current.CancellationToken);

        Assert.Null(loaded);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("/absolute")]
    [InlineData("C:\\outside")]
    [InlineData("file:stream")]
    [InlineData("bad\0id")]
    public void ProfileIdsCannotEscapeTheProfilesDirectory(string id)
    {
        Assert.Throws<ArgumentException>(() => repository.GetProfilePath(id));
    }

    [Fact]
    public async Task InvalidStoredSelectionReadsAsMissingWithoutOpeningAnExternalPath()
    {
        Assert.Null(await repository.LoadAsync("../outside", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmbeddedIdCannotRedirectSubsequentWrites()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "racing.json"), "{\"id\":\"../outside\"}", TestContext.Current.CancellationToken);
        var profile = await repository.LoadAsync("racing", TestContext.Current.CancellationToken);
        Assert.NotNull(profile);
        Assert.Equal("racing", profile.Id);
        await repository.SaveAsync(profile, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExplicitNullRulesAreTreatedAsADamagedProfile()
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "racing.json"), "{\"rules\":null}", TestContext.Current.CancellationToken);
        Assert.Null(await repository.LoadAsync("racing", TestContext.Current.CancellationToken));
        Assert.True(File.Exists(Path.Combine(directory, "racing.json.damaged")));
    }
}
