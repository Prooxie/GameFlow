using GameFlow.Core.Models;
using GameFlow.Core.Pipeline;
using GameFlow.Infrastructure.Runtime.Templates;

namespace GameFlow.Infrastructure.Runtime.Slots;

/// <summary>
/// One slot's live pipeline: an isolated
/// <see cref="ControllerMappingPipeline"/> feeding a dedicated
/// <see cref="IOutputSink"/>. The slot runtime ticks one of these per
/// enabled slot, passing the snapshot from the slot's assigned input
/// device. Each instance owns its sink and disposes it.
/// </summary>
public sealed class SlotPipeline : IAsyncDisposable
{
    private readonly ControllerMappingPipeline pipeline;
    private readonly IOutputSink outputSink;

    public SlotPipeline(string slotId, IReadOnlyList<string> inputDeviceIds,
        DeviceOutputTemplate template, ControllerMappingPipeline pipeline, IOutputSink outputSink)
    {
        SlotId = slotId;
        InputDeviceIds = inputDeviceIds;
        Template = template;
        this.pipeline = pipeline;
        this.outputSink = outputSink;
    }

    public string SlotId { get; }

    /// <summary>Catalog ids of this slot's assigned input devices.</summary>
    public IReadOnlyList<string> InputDeviceIds { get; }

    /// <summary>The slot's output template (lighting, rumble, FFB, …).</summary>
    public DeviceOutputTemplate Template { get; }

    /// <summary>The device whose snapshot drives this slot (first assigned), or null.</summary>
    public string? PrimaryDeviceId => InputDeviceIds.Count > 0 ? InputDeviceIds[0] : null;

    public string OutputDisplayName => outputSink.DisplayName;

    /// <summary>
    /// (Vid, Pid) of the virtual device this slot's sink advertises to the
    /// OS, if any. The runtime aggregates this across every active slot
    /// so the input catalog can hide the app's own virtual outputs from
    /// the input-device list — without it, a ViGEm/HIDMaestro output
    /// re-appears as a brand-new "real" gamepad the moment it activates,
    /// selectable as input for this or another slot. That recursive
    /// wiring (a slot's own output feeding back in as its input, direct
    /// or via another slot) was a real cause of freezes, not a
    /// hypothetical one — see RuntimeCoordinator's aggregation of this.
    /// </summary>
    public (ushort Vid, ushort Pid)? OutputHardwareSignature => outputSink.OwnedHardwareSignature;

    /// <summary>
    /// Runs one frame: transforms <paramref name="input"/> through the
    /// slot's pipeline and writes the virtual snapshot to the slot's sink.
    /// </summary>
    public async ValueTask<ControllerFrameResult> ProcessAsync(
        ControllerSnapshot input, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var result = pipeline.Process(input, now);
        await outputSink.WriteAsync(result.VirtualSnapshot, cancellationToken);
        return result;
    }

    public ValueTask DisposeAsync() => outputSink.DisposeAsync();
}
