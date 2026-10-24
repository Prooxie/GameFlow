using System.Text;
using System.Text.Json;
using GameFlow.Core.Models;
using GameFlow.Core.Pipeline;
using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Localization;
using GameFlow.Infrastructure.Profiles;
using GameFlow.Infrastructure.Runtime;
using GameFlow.Infrastructure.Runtime.Effects;
using GameFlow.Infrastructure.Runtime.Slots;
using GameFlow.Infrastructure.Runtime.Templates;
using GameFlow.Infrastructure.Runtime.Web;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

/// <summary>
/// The external lighting API's policy: off by default, bearer token even on
/// loopback, no web-page origins, bounded and validated bodies, and a rate
/// limit — then the routes themselves.
/// </summary>
public sealed class LightingApiTests : IDisposable
{
    private const string Token = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string ValidAuthorization = "Bearer " + Token;

    private readonly string slotsFile = Path.Combine(
        Path.GetTempPath(), "gameflow-lighting-api-" + Guid.NewGuid().ToString("N") + ".json");
    private readonly SlotRegistry slots;
    private readonly InputDeviceCatalog catalog;
    private readonly ExternalLightingStore store = new();
    private readonly StubSettings settings = new();
    private readonly ManualClock clock = new();
    private readonly LightingApi api;
    private readonly string slotId;

    public LightingApiTests()
    {
        slots = new SlotRegistry(NullLogger<SlotRegistry>.Instance, slotsFile);
        slotId = slots.CreateSlot(VirtualControllerKind.DualSense)!.Id;
        slots.AssignDevice(slotId, "sdl-gamepad-ds");
        catalog = new InputDeviceCatalog(
            new Mock<ILocalizationService>().Object,
            new DeviceCategoryOverrideStore(NullLogger<DeviceCategoryOverrideStore>.Instance));
        catalog.ReplaceDevices("sdl", [new InputDeviceInfo("sdl-gamepad-ds", "DualSense", VendorId: 0x054C, ProductId: 0x0CE6)]);
        api = new LightingApi(store, slots, catalog, settings, clock);
        settings.Current = new AppSettings { ExternalLightingApiEnabled = true, WebAccessToken = Token };
    }

    public void Dispose() => File.Delete(slotsFile);

    private LightingApiResponse Send(
        string method,
        string path,
        string? body = null,
        string? authorization = ValidAuthorization,
        string? contentType = "application/json",
        bool loopback = true,
        bool originAllowed = true,
        bool allowsRemote = false,
        string? queryTarget = null)
    {
        return api.Handle(
            new LightingApiRequest(
                method,
                path,
                queryTarget,
                authorization,
                contentType,
                loopback,
                originAllowed,
                body is null ? ReadOnlyMemory<byte>.Empty : Encoding.UTF8.GetBytes(body)),
            allowsRemote,
            Token);
    }

    private LightingApiResponse Post(string body, string? authorization = ValidAuthorization, string? contentType = "application/json") =>
        Send("POST", LightingApi.LightingPath, body, authorization, contentType);

    [Fact]
    public void TheApiIsOffUntilTurnedOn()
    {
        settings.Current = settings.Current with { ExternalLightingApiEnabled = false };

        Assert.Equal(403, Post("""{"color":"#FF0000"}""").StatusCode);
        Assert.Null(store.Resolve(slotId, "sdl-gamepad-ds"));
    }

    [Fact]
    public void LoopbackStillNeedsTheBearerToken()
    {
        var response = Post("""{"color":"#FF0000"}""", authorization: null);

        Assert.Equal(401, response.StatusCode);
        Assert.True(response.ChallengeBearer);
    }

    [Fact]
    public void AWrongTokenIsRejected()
    {
        Assert.Equal(401, Post("""{"color":"#FF0000"}""", authorization: "Bearer " + new string('b', 64)).StatusCode);
    }

    [Fact]
    public void RemoteCallersNeedSharingEvenWithTheToken()
    {
        Assert.Equal(401, Send("POST", LightingApi.LightingPath, """{"color":"#FF0000"}""", loopback: false).StatusCode);
        Assert.Equal(204, Send("POST", LightingApi.LightingPath, """{"color":"#FF0000"}""", loopback: false, allowsRemote: true).StatusCode);
    }

    [Fact]
    public void WebPageOriginsAreRefused()
    {
        Assert.Equal(403, Send("POST", LightingApi.LightingPath, """{"color":"#FF0000"}""", originAllowed: false).StatusCode);
    }

    [Fact]
    public void ABodyMustBeDeclaredAsJson()
    {
        Assert.Equal(415, Post("""{"color":"#FF0000"}""", contentType: "text/plain").StatusCode);
        Assert.Equal(204, Post("""{"color":"#FF0000"}""", contentType: "application/json; charset=utf-8").StatusCode);
    }

    [Fact]
    public void AnOversizedBodyIsRefused()
    {
        var huge = "{\"color\":\"#FF0000\",\"pad\":\"" + new string('x', LightingApi.MaxBodyBytes) + "\"}";

        Assert.Equal(413, Post(huge).StatusCode);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"color":"red"}""")]
    [InlineData("""{"color":"#FF0000","mode":"disco"}""")]
    [InlineData("""{"color":"#FF0000","brightness":2}""")]
    [InlineData("""{"color":"#FF0000","ttlMs":0}""")]
    [InlineData("""{"color":"#FF0000","ttlMs":3600001}""")]
    [InlineData("""{"mode":"solid"}""")]
    public void InvalidBodiesAreRejectedWithAReason(string body)
    {
        var response = Post(body);

        Assert.Equal(400, response.StatusCode);
        using var json = JsonDocument.Parse(response.Json!);
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("error").GetString()));
    }

    [Fact]
    public void ASolidRequestForAllReachesEveryPad()
    {
        Assert.Equal(204, Post("""{"target":"all","color":"#FF8800","brightness":0.5,"ttlMs":2000}""").StatusCode);

        var request = store.Resolve(slotId, "sdl-gamepad-ds");
        Assert.Equal(new ExternalLightRequest(LightbarMode.Solid, new LightColor(0xFF, 0x88, 0x00), 0.5f), request);
    }

    [Theory]
    [InlineData("1")]
    [InlineData("sdl-gamepad-ds")]
    public void SlotNumbersAndDeviceIdsAreTargets(string target)
    {
        Assert.Equal(204, Post($$"""{"target":"{{target}}","mode":"rainbow"}""").StatusCode);

        Assert.Equal(LightbarMode.Rainbow, store.Resolve(slotId, "sdl-gamepad-ds")?.Mode);
    }

    [Fact]
    public void AnUnknownTargetIsNotFound()
    {
        Assert.Equal(404, Post("""{"target":"nope","color":"#FF0000"}""").StatusCode);
    }

    [Fact]
    public void DeleteHandsTheLightsBack()
    {
        _ = Post("""{"color":"#FF0000"}""");

        Assert.Equal(204, Send("DELETE", LightingApi.LightingPath, queryTarget: "all").StatusCode);
        Assert.Null(store.Resolve(slotId, "sdl-gamepad-ds"));
    }

    [Fact]
    public void TargetsListsAllSlotsAndAssignedPads()
    {
        var response = Send("GET", LightingApi.TargetsPath);

        Assert.Equal(200, response.StatusCode);
        using var json = JsonDocument.Parse(response.Json!);
        var kinds = json.RootElement.GetProperty("targets").EnumerateArray()
            .Select(t => t.GetProperty("kind").GetString())
            .ToArray();
        Assert.Equal(new[] { "all", "slot", "device" }, kinds);
    }

    [Fact]
    public void WrongMethodsGetAllowHeaders()
    {
        var response = Send("PUT", LightingApi.LightingPath);

        Assert.Equal(405, response.StatusCode);
        Assert.Equal("POST, DELETE", response.Allow);
    }

    [Fact]
    public void BurstsAreRateLimitedPerSecond()
    {
        for (var i = 0; i < LightingApi.MaxRequestsPerSecond; i++)
        {
            Assert.Equal(200, Send("GET", LightingApi.TargetsPath).StatusCode);
        }

        var limited = Send("GET", LightingApi.TargetsPath);
        Assert.Equal(429, limited.StatusCode);
        Assert.Equal(1, limited.RetryAfterSeconds);

        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(200, Send("GET", LightingApi.TargetsPath).StatusCode);
    }

    [Fact]
    public void OnlyApiPathsAreClaimed()
    {
        Assert.True(LightingApi.IsApiPath("/api/v1/lighting"));
        Assert.False(LightingApi.IsApiPath("/"));
        Assert.False(LightingApi.IsApiPath("/overlay"));
        Assert.False(LightingApi.IsApiPath("/apiary"));
    }

    [Theory]
    [InlineData(true, false, ValidAuthorization, true)]
    [InlineData(true, false, null, false)]                // loopback is not enough on its own
    [InlineData(true, false, Token, false)]               // the scheme is required
    [InlineData(false, false, ValidAuthorization, false)] // remote without sharing
    [InlineData(false, true, ValidAuthorization, true)]
    [InlineData(false, true, "Bearer wrong", false)]
    public void ApiAuthorizationRequiresTheBearerHeader(bool loopback, bool sharing, string? header, bool expected)
    {
        Assert.Equal(expected, WebAccessPolicy.IsApiAuthorized(loopback, sharing, Token, header));
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch.AddDays(1);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance(TimeSpan by) => now += by;
    }

    private sealed class StubSettings : IUserSettingsService
    {
        public AppSettings Current { get; set; } = new();

        public event EventHandler<UserSettingsChangedEventArgs>? Changed { add { } remove { } }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Func<AppSettings, AppSettings> change, CancellationToken cancellationToken = default)
        {
            Current = change(Current);
            return Task.CompletedTask;
        }
    }
}
