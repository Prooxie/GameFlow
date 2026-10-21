using GameFlow.Infrastructure.Runtime.HidMaestro;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.HidMaestro.Tests;

/// <summary>
/// The backend decides the status from what the search roots hold and from
/// elevation. The tests inject both, and the clock, so no test needs admin.
/// </summary>
public sealed class HidMaestroBackendTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    private readonly string temp = Path.Combine(Path.GetTempPath(), "gf-hm-" + Guid.NewGuid().ToString("N"));

    public HidMaestroBackendTests() => Directory.CreateDirectory(temp);

    public void Dispose()
    {
        if (Directory.Exists(temp))
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static HidMaestroBackend Backend(
        Func<IReadOnlyList<string>> roots, bool elevated = true, Func<DateTimeOffset>? now = null) =>
        new(NullLogger.Instance, roots, () => elevated, now ?? (() => Start));

    [WindowsFact]
    public void GetStatus_NoSdkInRoots_IsSdkMissing()
    {
        var status = Backend(() => [temp]).GetStatus();

        Assert.Equal(HidMaestroState.SdkMissing, status.State);
        Assert.Contains("HIDMaestro.Core.dll not found", status.Description);
    }

    [WindowsFact]
    public void GetStatus_SdkMissing_SearchesAtMostEvery10Seconds()
    {
        var calls = 0;
        var now = Start;
        var backend = Backend(() => { calls++; return [temp]; }, now: () => now);

        _ = backend.GetStatus();
        now = Start.AddSeconds(9);
        _ = backend.GetStatus();
        Assert.Equal(1, calls);

        now = Start.AddSeconds(10);
        _ = backend.GetStatus();
        Assert.Equal(2, calls);
    }

    [WindowsFact]
    public void GetStatus_LookupThrows_IsFailedAndSearchesAgainLater()
    {
        var calls = 0;
        var now = Start;
        var backend = Backend(() => { calls++; throw new IOException("disk gone"); }, now: () => now);

        var status = backend.GetStatus();
        now = Start.AddSeconds(10);
        _ = backend.GetStatus();

        Assert.Equal(HidMaestroState.Failed, status.State);
        Assert.Equal("HIDMaestro failed to start: disk gone", status.Description);
        Assert.Equal(2, calls);
    }

    [WindowsFact]
    public void GetStatus_UnreadableSdk_IsUnsupportedAndNamesTheRange()
    {
        File.WriteAllBytes(Path.Combine(temp, HidMaestroSdkLocator.FileName), new byte[16]);

        var status = Backend(() => [temp]).GetStatus();

        Assert.Equal(HidMaestroState.SdkVersionUnsupported, status.State);
        Assert.Contains("(unreadable)", status.Description);
        Assert.Contains("2.0.1 or a later 2.x", status.Description);
    }

    [WindowsFact]
    public void GetStatus_NotElevated_StillListsTheCatalog()
    {
        var backend = Backend(() => [AppContext.BaseDirectory], elevated: false);

        var status = backend.GetStatus();

        Assert.Equal(HidMaestroState.NotElevated, status.State);
        Assert.Contains("Administrator", status.Description);
        Assert.NotEmpty(backend.GetCatalog());
    }

    [WindowsFact]
    public void TryCreatePad_NotElevated_FailsWithAdministratorText()
    {
        var backend = Backend(() => [AppContext.BaseDirectory], elevated: false);

        var created = backend.TryCreatePad(new HidMaestroPadRequest("xbox-360-wired"), out var pad, out var failure);

        Assert.False(created);
        Assert.Null(pad);
        Assert.Contains("Administrator", failure);
    }

    [WindowsFact]
    public void DescribeFailure_UnauthorizedAccess_SaysAdministrator()
    {
        var text = HidMaestroSession.DescribeFailure(new UnauthorizedAccessException("denied"));

        Assert.Contains("Administrator", text);
        Assert.Contains("failed", text);
    }

    [WindowsFact]
    public void DescribeFailure_Other_NamesTheTypeAndSaysFailed()
    {
        var text = HidMaestroSession.DescribeFailure(new TimeoutException("too slow"));

        Assert.Contains(nameof(TimeoutException), text);
        Assert.Contains("failed", text);
        Assert.Contains("too slow", text);
    }
}
