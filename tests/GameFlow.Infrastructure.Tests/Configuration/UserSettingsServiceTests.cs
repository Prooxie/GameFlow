using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Profiles;
using GameFlow.Infrastructure.Tests.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Configuration;

/// <summary>Tests for <see cref="UserSettingsService"/>.</summary>
public sealed class UserSettingsServiceTests
{
    private readonly InMemoryProfileRepository repository = new();

    private UserSettingsService CreateService(InMemoryProfileRepository? source = null) =>
        new(source ?? repository, new NoLogLevelSwitch(), NullLogger<UserSettingsService>.Instance);

    [Fact]
    public async Task UpdateAsyncChangeSavesNewSettingsAsync()
    {
        var service = CreateService();
        await service.InitializeAsync(TestContext.Current.CancellationToken);

        await service.UpdateAsync(settings => settings with { MotionServerEnabled = true }, TestContext.Current.CancellationToken);

        Assert.True(repository.SavedSettings.MotionServerEnabled);
        Assert.True(service.Current.MotionServerEnabled);
    }

    [Fact]
    public async Task UpdateAsyncTwoChangesKeepBothFieldsAsync()
    {
        var service = CreateService();
        await service.InitializeAsync(TestContext.Current.CancellationToken);

        await service.UpdateAsync(settings => settings with { MotionServerEnabled = true }, TestContext.Current.CancellationToken);
        await service.UpdateAsync(settings => settings with { ActiveProfileId = "racing" }, TestContext.Current.CancellationToken);

        Assert.True(repository.SavedSettings.MotionServerEnabled);
        Assert.Equal("racing", repository.SavedSettings.ActiveProfileId);
    }

    /// <summary>
    /// Each change must apply to the latest settings, also when two writers run at the same time.
    /// </summary>
    [Fact]
    public async Task UpdateAsyncConcurrentChangesKeepBothFieldsAsync()
    {
        var service = CreateService();
        await service.InitializeAsync(TestContext.Current.CancellationToken);

        await Task.WhenAll(
            Task.Run(() => service.UpdateAsync(settings => settings with { MotionServerEnabled = true }, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken),
            Task.Run(() => service.UpdateAsync(settings => settings with { SelectedCulture = "cs" }, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken));

        Assert.True(repository.SavedSettings.MotionServerEnabled);
        Assert.Equal("cs", repository.SavedSettings.SelectedCulture);
    }

    [Fact]
    public async Task UpdateAsyncChangeRaisesChangedWithBothValuesAsync()
    {
        var service = CreateService();
        await service.InitializeAsync(TestContext.Current.CancellationToken);
        UserSettingsChangedEventArgs? raised = null;
        service.Changed += (_, args) => raised = args;

        await service.UpdateAsync(settings => settings with { MotionServerEnabled = true }, TestContext.Current.CancellationToken);

        Assert.NotNull(raised);
        Assert.False(raised.Previous.MotionServerEnabled);
        Assert.True(raised.Current.MotionServerEnabled);
    }

    [Fact]
    public async Task UpdateAsyncNoChangeDoesNotSaveAsync()
    {
        var service = CreateService();
        await service.InitializeAsync(TestContext.Current.CancellationToken);
        var raised = false;
        service.Changed += (_, _) => raised = true;

        await service.UpdateAsync(settings => settings, TestContext.Current.CancellationToken);

        Assert.False(raised);
    }

    [Fact]
    public async Task InitializeAsyncDamagedFileUsesDefaultsAsync()
    {
        var damaged = new InMemoryProfileRepository { LoadSettingsFailure = new System.Text.Json.JsonException("bad") };
        var service = CreateService(damaged);

        await service.InitializeAsync(TestContext.Current.CancellationToken);

        Assert.Equal(new AppSettings(), service.Current with { WebAccessToken = string.Empty });
        Assert.Equal(64, service.Current.WebAccessToken.Length);
    }
}
