using GameFlow.Infrastructure.Runtime.HidMaestro;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.HidMaestro;

public sealed class HidMaestroProfileCatalogServiceTests
{
    private static HidMaestroCatalogProfile Profile(string id, string type = "gamepad") =>
        new(id, id, "Vendor", 0, 0, 12, 6, true, "usb", true, type);

    private static HidMaestroProfileCatalogService CreateService(FakeHidMaestroBackend backend) =>
        new(NullLogger<HidMaestroProfileCatalogService>.Instance, backend);

    [Theory]
    [InlineData("gamepad", true)]
    [InlineData("arcadestick", true)]
    [InlineData("ARCADESTICK", true)]
    [InlineData("wheel", false)]
    [InlineData("mouse", false)]
    [InlineData("keyboard", false)]
    [InlineData("hotas", false)]
    [InlineData("pedals", false)]
    [InlineData("", false)]
    public void IsGameControllerProfile_UsesTheCatalogType(string type, bool expected)
    {
        Assert.Equal(expected, HidMaestroProfileCatalogService.IsGameControllerProfile(Profile("any", type)));
    }

    [Fact]
    public void IsGameControllerProfile_NullType_IsNotAGameController()
    {
        Assert.False(HidMaestroProfileCatalogService.IsGameControllerProfile(Profile("any", null!)));
    }

    [Fact]
    public void IsGameControllerProfile_IgnoresNamesThatLookSpecialized()
    {
        // The old keyword rule hid this pad. The Type field is the only signal now.
        var profile = new HidMaestroCatalogProfile("racing-pad", "Racing Pad", "Flight Co", 0, 0, 12, 6, true, "usb", true, "gamepad");

        Assert.True(HidMaestroProfileCatalogService.IsGameControllerProfile(profile));
    }

    [Fact]
    public async Task GetProfilesAsync_CatalogAvailable_ReturnsTheBackendCatalog()
    {
        var backend = new FakeHidMaestroBackend();
        backend.Catalog.Add(Profile("a"));
        backend.Catalog.Add(Profile("b", "wheel"));

        var profiles = await CreateService(backend).GetProfilesAsync();

        Assert.Equal(["a", "b"], profiles.Select(p => p.Id));
    }

    [Fact]
    public async Task GetProfilesAsync_NotElevated_StillReturnsTheBackendCatalog()
    {
        var backend = new FakeHidMaestroBackend { Status = new(HidMaestroState.NotElevated, "not elevated") };
        backend.Catalog.Add(Profile("a"));

        var profiles = await CreateService(backend).GetProfilesAsync();

        Assert.Equal(["a"], profiles.Select(p => p.Id));
    }

    [Fact]
    public async Task GetProfilesAsync_SdkMissing_ReturnsFallbackProfiles()
    {
        var backend = new FakeHidMaestroBackend { Status = new(HidMaestroState.SdkMissing, "missing") };
        backend.Catalog.Add(Profile("ignored"));

        var profiles = await CreateService(backend).GetProfilesAsync();

        Assert.Same(HidMaestroProfileCatalogService.FallbackProfiles, profiles);
    }

    [Fact]
    public async Task GetProfilesAsync_EmptyCatalogWhileReady_ReturnsFallbackProfiles()
    {
        var profiles = await CreateService(new FakeHidMaestroBackend()).GetProfilesAsync();

        Assert.Same(HidMaestroProfileCatalogService.FallbackProfiles, profiles);
    }

    [Fact]
    public async Task GetProfilesAsync_CachesTheFirstEnumeration()
    {
        var backend = new FakeHidMaestroBackend();
        backend.Catalog.Add(Profile("a"));
        var service = CreateService(backend);

        var first = await service.GetProfilesAsync();
        backend.Catalog.Add(Profile("b"));
        var second = await service.GetProfilesAsync();

        Assert.Same(first, second);
        Assert.Single(second);
    }
}
