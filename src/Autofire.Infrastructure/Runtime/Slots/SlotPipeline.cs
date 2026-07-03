using Autofire.Core.Models;
using Autofire.Core.Pipeline;
using Autofire.Infrastructure.Runtime.Templates;

namespace Autofire.Infrastructure.Runtime.Slots;

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
