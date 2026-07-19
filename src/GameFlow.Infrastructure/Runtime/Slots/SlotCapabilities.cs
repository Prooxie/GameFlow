using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.Templates;

namespace GameFlow.Infrastructure.Runtime.Slots;

/// <summary>
/// Optional capability for an <see cref="IInputSource"/> that can read
/// several specific devices by id within one tick — required for slot
/// mode, where each slot consumes its own assigned device. Implementers
/// pump the backend once via <see cref="PumpForSlots"/>, then answer
/// per-device reads via <see cref="ReadDevice"/>.
/// </summary>
public interface IMultiDeviceInputSource
{
    /// <summary>Advances the backend once per tick (before per-slot reads).</summary>
    void PumpForSlots();

    /// <summary>
    /// Reads the current snapshot for one device id. Returns an empty
    /// snapshot if the device can't be opened or isn't present.
    /// </summary>
    ControllerSnapshot ReadDevice(string deviceId);
}

/// <summary>
/// Optional capability for an <see cref="IOutputSink"/> whose emitted
/// virtual controller is described by a <see cref="DeviceOutputTemplate"/>
/// (e.g. the HIDMaestro sink). The slot runtime calls
/// <see cref="Configure"/> with the slot's template before ticking it.
/// </summary>
public interface IConfigurableOutputSink
{
    void Configure(DeviceOutputTemplate template);
}

/// <summary>
/// Null-object <see cref="IMultiDeviceInputSource"/> used when the
/// top-level input source doesn't support per-device reads (e.g. the
/// profile's input provider is "none" or "demo", or SDL failed to
/// initialize). Slots still tick against it: device-fed slots read as
/// empty (accurately — there's nothing to read), while demo-preview
/// slots animate normally, because the demo waveform never touches the
/// input source at all. Before this existed, a non-multi-device source
/// silently disabled ALL slot processing — which surfaced to users as
/// "demo preview does nothing" with zero feedback.
/// </summary>
public sealed class EmptyMultiDeviceInputSource : IMultiDeviceInputSource
{
    public static EmptyMultiDeviceInputSource Instance { get; } = new();

    private EmptyMultiDeviceInputSource() { }

    public void PumpForSlots() { }

    public ControllerSnapshot ReadDevice(string deviceId) =>
        ControllerSnapshot.Empty(deviceId) with { Timestamp = DateTimeOffset.UtcNow };
}
