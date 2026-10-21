using GameFlow.Infrastructure.Runtime.HidMaestro;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.HidMaestro;

public sealed class UnavailableHidMaestroBackendTests
{
    private readonly UnavailableHidMaestroBackend backend = new();

    [Fact]
    public void GetStatus_IsNotWindowsOffWindows_ElseFailed()
    {
        var expected = OperatingSystem.IsWindows() ? HidMaestroState.Failed : HidMaestroState.NotWindows;

        Assert.Equal(expected, backend.GetStatus().State);
        Assert.False(backend.GetStatus().CanCreatePads);
        Assert.False(backend.GetStatus().CanListCatalog);
    }

    [Fact]
    public void GetCatalog_IsEmpty() => Assert.Empty(backend.GetCatalog());

    [Fact]
    public void TryCreatePad_Fails_WithTheStatusDescription()
    {
        var created = backend.TryCreatePad(new HidMaestroPadRequest("xbox-360-wired"), out var pad, out var failure);

        Assert.False(created);
        Assert.Null(pad);
        Assert.Equal(backend.GetStatus().Description, failure);
    }

    [Fact]
    public void FinalizeNames_DoesNothing() => backend.FinalizeNames();
}
