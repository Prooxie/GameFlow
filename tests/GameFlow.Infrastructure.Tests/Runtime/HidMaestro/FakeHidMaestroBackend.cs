using System.Diagnostics.CodeAnalysis;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.HidMaestro;

namespace GameFlow.Infrastructure.Tests.Runtime.HidMaestro;

/// <summary>
/// Scripted <see cref="IHidMaestroBackend"/> for tests that must not touch the SDK.
/// Tests set <see cref="Status"/>, fill <see cref="Catalog"/>, and read
/// <see cref="Requests"/> back to see what the code under test asked for.
/// </summary>
internal sealed class FakeHidMaestroBackend : IHidMaestroBackend
{
    public HidMaestroStatus Status { get; set; } = new(HidMaestroState.Ready, "fake ready");

    public List<HidMaestroCatalogProfile> Catalog { get; } = [];

    public List<HidMaestroPadRequest> Requests { get; } = [];

    /// <summary>Builds the pad for a request. Returning null makes <see cref="TryCreatePad"/> fail.</summary>
    public Func<HidMaestroPadRequest, FakeHidMaestroPad?> CreatePad { get; set; } =
        request => new FakeHidMaestroPad { ProfileId = request.ProfileId };

    public int FinalizeNamesCalls { get; private set; }

    public HidMaestroStatus GetStatus() => Status;

    public IReadOnlyList<HidMaestroCatalogProfile> GetCatalog() => Status.CanListCatalog ? [.. Catalog] : [];

    public bool TryCreatePad(HidMaestroPadRequest request, [NotNullWhen(true)] out IHidMaestroPad? pad, out string? failure)
    {
        Requests.Add(request);

        if (!Status.CanCreatePads)
        {
            pad = null;
            failure = Status.Description;
            return false;
        }

        pad = CreatePad(request);
        if (pad is null)
        {
            failure = "fake failure";
            return false;
        }

        failure = null;
        return true;
    }

    public void FinalizeNames() => FinalizeNamesCalls++;
}

/// <summary>A pad that counts frames and can be told to fail or to raise rumble.</summary>
internal sealed class FakeHidMaestroPad : IHidMaestroPad
{
    public string ProfileId { get; init; } = "xbox-360-wired";

    public string ProfileName { get; init; } = "Fake pad";

    public (ushort Vid, ushort Pid) HardwareSignature { get; init; } = (0x045E, 0x028E);

    public bool CarriesExtendedInput { get; init; }

    public int Submits { get; private set; }

    public bool ThrowOnSubmit { get; set; }

    public bool Disposed { get; private set; }

    public event Action<double, double>? RumbleReceived;

    public void RaiseRumble(double low, double high) => RumbleReceived?.Invoke(low, high);

    public void Submit(ControllerSnapshot snapshot)
    {
        Submits++;
        if (ThrowOnSubmit)
        {
            throw new InvalidOperationException("fake submit failure");
        }
    }

    public void Dispose() => Disposed = true;
}
