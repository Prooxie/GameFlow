using System.Diagnostics.CodeAnalysis;
using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// The seam between GameFlow and the HIDMaestro SDK. Infrastructure
/// depends only on this interface. The typed adapter project implements
/// it, so the SDK never leaks into code that has to build without it.
/// </summary>
public interface IHidMaestroBackend
{
    /// <summary>Whether pads can be created, and if not, why.</summary>
    HidMaestroStatus GetStatus();

    /// <summary>The SDK's profile catalog. Empty unless <see cref="HidMaestroStatus.CanListCatalog"/>.</summary>
    IReadOnlyList<HidMaestroCatalogProfile> GetCatalog();

    /// <summary>
    /// Creates one virtual pad. Returns false with a reason instead of
    /// throwing, because "no driver" is an expected state, not a fault.
    /// </summary>
    bool TryCreatePad(HidMaestroPadRequest request, [NotNullWhen(true)] out IHidMaestroPad? pad, out string? failure);

    /// <summary>Gives the pads created so far their final Windows device names.</summary>
    void FinalizeNames();
}

/// <summary>One live virtual pad. Disposing it removes the device.</summary>
public interface IHidMaestroPad : IDisposable
{
    /// <summary>The catalog id of the profile the pad emulates.</summary>
    string ProfileId { get; }

    /// <summary>The display name of that profile.</summary>
    string ProfileName { get; }

    /// <summary>The real identity of the emitted device, read from the deployed profile.</summary>
    (ushort Vid, ushort Pid) HardwareSignature { get; }

    /// <summary>True when the pad can carry motion and touchpad data.</summary>
    bool CarriesExtendedInput { get; }

    /// <summary>Raised when a game sends rumble. The arguments are the large and small motor strength.</summary>
    event Action<double, double>? RumbleReceived;

    /// <summary>Writes one frame. Throws when the SDK rejects the frame.</summary>
    void Submit(ControllerSnapshot snapshot);
}

/// <summary>What to create. <see cref="Generic"/> is set only for a profile built from a shape.</summary>
public sealed record HidMaestroPadRequest(string ProfileId, HidMaestroGenericShape? Generic = null);

/// <summary>The shape of a generic DirectInput pad, built at runtime instead of read from the catalog.</summary>
public sealed record HidMaestroGenericShape(
    string ProductString,
    ushort VendorId,
    ushort ProductId,
    int Thumbsticks,
    int Triggers,
    int Buttons,
    int Povs);
