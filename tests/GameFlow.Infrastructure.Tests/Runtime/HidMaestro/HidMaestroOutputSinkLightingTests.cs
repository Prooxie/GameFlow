using GameFlow.Core.Models;
using GameFlow.Core.Pipeline;
using GameFlow.Infrastructure.Runtime.Effects;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using GameFlow.Infrastructure.Runtime.Templates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.HidMaestro;

/// <summary>The sink re-raises the game's lighting from whichever pad is live, and lets go of it on teardown.</summary>
public sealed class HidMaestroOutputSinkLightingTests
{
    [Fact]
    public async Task LightingFromTheLivePadIsReRaisedAndReleasedOnDispose()
    {
        FakeHidMaestroPad? created = null;
        var backend = new FakeHidMaestroBackend
        {
            CreatePad = request => created = new FakeHidMaestroPad { ProfileId = request.ProfileId },
        };
        backend.Catalog.AddRange(HidMaestroProfileCatalogService.FallbackProfiles);

        var sink = new HidMaestroOutputSink(NullLogger<HidMaestroOutputSink>.Instance, backend, TimeProvider.System);
        sink.Configure(new DeviceOutputTemplate { OutputKind = VirtualControllerKind.DualSense });
        await sink.WriteAsync(ControllerSnapshot.Empty("test"), TestContext.Current.CancellationToken);

        var received = new List<GameLighting>();
        sink.LightingReceived += received.Add;

        Assert.NotNull(created);
        created!.RaiseLighting(new GameLighting(Color: new LightColor(1, 2, 3)));

        var lighting = Assert.Single(received);
        Assert.Equal(new LightColor(1, 2, 3), lighting.Color);
        Assert.True(created.HasLightingSubscribers);

        await sink.DisposeAsync();
        Assert.False(created.HasLightingSubscribers);
    }
}
