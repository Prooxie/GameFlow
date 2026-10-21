using System.Diagnostics.CodeAnalysis;

namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// The backend used when no adapter is registered. It reports why
/// HIDMaestro is unavailable and creates nothing.
/// </summary>
public sealed class UnavailableHidMaestroBackend : IHidMaestroBackend
{
    private static readonly HidMaestroStatus Status = OperatingSystem.IsWindows()
        ? new HidMaestroStatus(HidMaestroState.Failed, "the HIDMaestro adapter is not registered")
        : new HidMaestroStatus(HidMaestroState.NotWindows, "HIDMaestro is only available on Windows.");

    /// <inheritdoc />
    public HidMaestroStatus GetStatus() => Status;

    /// <inheritdoc />
    public IReadOnlyList<HidMaestroCatalogProfile> GetCatalog() => [];

    /// <inheritdoc />
    public bool TryCreatePad(HidMaestroPadRequest request, [NotNullWhen(true)] out IHidMaestroPad? pad, out string? failure)
    {
        pad = null;
        failure = Status.Description;
        return false;
    }

    /// <inheritdoc />
    public void FinalizeNames()
    {
    }
}
