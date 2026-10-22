using GameFlow.App.Bootstrap;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.HidMaestro;

/// <summary>
/// Runs the real backend from the app host in a folder with no SDK. This is
/// the common user state. Any early reference to an SDK type fails here with
/// a <see cref="FileNotFoundException"/>, because this output has no SDK DLL.
/// </summary>
public sealed class NoSdkHostTests
{
    [Fact]
    public void HostBackend_WithoutTheSdk_IsTheAdapterAndReportsSdkMissingWithoutThrowing()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The HIDMaestro adapter is registered only on Windows.");
        Assert.SkipWhen(
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAMEFLOW_HIDMAESTRO_DIR")),
            "GAMEFLOW_HIDMAESTRO_DIR is set, so the backend can find an SDK.");
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "HIDMaestro.Core.dll")));
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "HIDMaestro", "HIDMaestro.Core.dll")));

        using var host = HostBuilderFactory.Create([]).Build();
        var backend = host.Services.GetRequiredService<IHidMaestroBackend>();

        Assert.IsNotType<UnavailableHidMaestroBackend>(backend);

        var status = backend.GetStatus();
        Assert.Equal(HidMaestroState.SdkMissing, status.State);
        Assert.Contains("HIDMaestro.Core.dll not found", status.Description, StringComparison.Ordinal);

        Assert.Empty(backend.GetCatalog());

        Assert.False(backend.TryCreatePad(new HidMaestroPadRequest("xbox-360-wired"), out var pad, out var failure));
        Assert.Null(pad);
        Assert.Contains("not found", failure, StringComparison.Ordinal);

        backend.FinalizeNames();
    }
}
