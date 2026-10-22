using System.Net;
using System.Net.Sockets;
using GameFlow.App.Bootstrap;
using GameFlow.App.ViewModels;
using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Runtime.Motion;
using GameFlow.Infrastructure.Tests.Configuration;
using GameFlow.Infrastructure.Tests.Profiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Motion;

public sealed class MotionServerAccessTests
{
    [Fact]
    public async Task DsuStartsOnLoopbackAndRebindsOnlyAfterRemoteOptIn()
    {
        var settings = CreateSettings();
        int port;
        using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) { port = ((IPEndPoint)probe.Client.LocalEndPoint!).Port; }
        await settings.UpdateAsync(current => current with { MotionServerEnabled = true, MotionServerPort = port }, TestContext.Current.CancellationToken);
        using var host = Host(settings);
        var server = host.Services.GetRequiredService<DsuServer>();
        try
        {
            await server.StartAsync(TestContext.Current.CancellationToken);
            await Until(() => server.IsRunning);
            Assert.Equal($"127.0.0.1:{port}", server.ListenEndpoint);
            await settings.UpdateAsync(current => current with { MotionServerAllowRemote = true }, TestContext.Current.CancellationToken);
            await Until(() => server.ListenEndpoint == $"0.0.0.0:{port}");
            await settings.UpdateAsync(current => current with { MotionServerEnabled = false }, TestContext.Current.CancellationToken);
            await Until(() => !server.IsRunning);
        }
        finally { await server.StopAsync(TestContext.Current.CancellationToken); }
    }

    [Fact]
    public async Task BadPortCannotPreventDisablingNetworkServices()
    {
        var settings = CreateSettings();
        await settings.UpdateAsync(current => current with { MotionServerEnabled = true, MotionServerAllowRemote = true }, TestContext.Current.CancellationToken);
        using var host = Host(settings);
        var vm = host.Services.GetRequiredService<MotionServerPanelViewModel>();
        vm.Port = "invalid";
        vm.IsEnabled = false;
        vm.AllowRemote = false;
        await Until(() => !settings.Current.MotionServerEnabled && !settings.Current.MotionServerAllowRemote);
        Assert.True(vm.HasPortError);
        Assert.Equal(26760, settings.Current.MotionServerPort);
    }

    private static UserSettingsService CreateSettings() => new(new InMemoryProfileRepository(), new NoLogLevelSwitch(), NullLogger<UserSettingsService>.Instance);
    private static IHost Host(IUserSettingsService settings) => HostBuilderFactory.Create([]).ConfigureServices(services => services.AddSingleton(settings)).Build();
    private static async Task Until(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 4000;
        while (!condition() && Environment.TickCount64 < deadline) { await Task.Delay(10, TestContext.Current.CancellationToken); }
        Assert.True(condition(), "Server did not reach the expected state within four seconds.");
    }
}
