using GameFlow.Core.Models;
using GameFlow.Core.Pipeline;
using GameFlow.Infrastructure.Profiles;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Slots;

/// <summary>
/// Drives the multi-slot runtime: one independent <see cref="SlotPipeline"/>
/// per enabled <see cref="ControllerSlot"/>. The coordinator pumps the
/// shared input source once per tick, then this orchestrator reads each
/// slot's assigned device and runs its pipeline → sink. Per-slot failures
/// are caught and logged so one bad slot can't stall the others.
///
/// <para>Created and ticked by <see cref="RuntimeCoordinator"/> only when
/// the input source supports <see cref="IMultiDeviceInputSource"/> and at
/// least one enabled slot exists; otherwise the coordinator keeps running
/// its original single pipeline.</para>
/// </summary>
public sealed class SlotRuntime : IAsyncDisposable
{
    private readonly SlotRegistry registry;
    private readonly IOutputSinkFactory outputSinkFactory;
    private readonly SlotSnapshotStore snapshotStore;
    private readonly IProfileRepository profileRepository;
    private readonly ILogger logger;

    private readonly List<SlotPipeline> pipelines = [];
    private readonly Dictionary<string, int> slotFailureCounts = new(StringComparer.Ordinal);

    public SlotRuntime(SlotRegistry registry, IOutputSinkFactory outputSinkFactory,
        SlotSnapshotStore snapshotStore,
        IProfileRepository profileRepository, ILogger logger)
    {
        this.registry = registry;
        this.outputSinkFactory = outputSinkFactory;
        this.snapshotStore = snapshotStore;
        this.profileRepository = profileRepository;
        this.logger = logger;
    }

    /// <summary>True if there is at least one enabled slot to run.</summary>
    public bool HasEnabledSlots => registry.GetSlots().Any(s => s.Enabled);

    /// <summary>
    /// (Vid, Pid) signatures of every currently-active slot's virtual
    /// output device, deduplicated. RuntimeCoordinator combines this with
    /// the top-level sink's own signature and feeds the result to the
    /// input catalog every tick, so none of this app's own virtual
    /// outputs can ever be enumerated (and therefore selected) as an
    /// input device — see SlotPipeline.OutputHardwareSignature.
    /// </summary>
    public IReadOnlyList<(ushort Vid, ushort Pid)> GetActiveOutputSignatures() =>
        [.. pipelines
            .Select(p => p.OutputHardwareSignature)
            .Where(sig => sig is not null)
            .Select(sig => sig!.Value)
            .Distinct()];

    /// <summary>
    /// Tears down existing slot pipelines and builds fresh ones for the
    /// current enabled slots, using <paramref name="activeProfile"/> for
    /// the mapping pipeline and <paramref name="outputProvider"/> for each
    /// slot's sink. Each sink is configured from its slot's template.
    /// </summary>
    public async Task RebuildAsync(ProfileDocument activeProfile, string outputProvider)
    {
        var rebuildSw = System.Diagnostics.Stopwatch.StartNew();
        var enabledCount = 0;
        foreach (var s in registry.GetSlots()) { if (s.Enabled) enabledCount++; }
        logger.LogInformation(
            "Slot runtime: RebuildAsync starting (enabledSlots={EnabledSlots}, totalSlots={TotalSlots}, output={Output}).",
            enabledCount, registry.GetSlots().Count, outputProvider);

        await DisposePipelinesAsync();

        foreach (var slot in registry.GetSlots())
        {
            if (!slot.Enabled)
            {
                continue;
            }

            try
            {
                logger.LogInformation(
                    "Slot runtime: building pipeline for slot {SlotId} ({SlotName}) " +
                    "(profiles={ProfileCount}, devices={DeviceCount}).",
                    slot.Id, slot.Name, slot.ProfileIds.Count, slot.InputDeviceIds.Count);

                var profileSw = System.Diagnostics.Stopwatch.StartNew();
                var slotProfile = await ResolveSlotProfileAsync(slot);
                profileSw.Stop();
                logger.LogInformation(
                    "Slot runtime: ResolveSlotProfileAsync completed for slot {SlotId} in {ElapsedMs} ms.",
                    slot.Id, profileSw.ElapsedMilliseconds);

                // Per-slot output provider wins; an empty value means this
                // slot was saved before per-slot providers existed (or was
                // never explicitly set), so it inherits the profile-level
                // pick — the ONLY behavior available before this field
                // existed. Every slot's chosen backend is logged so it's
                // never a mystery which one actually ended up active.
                var slotOutputProvider = string.IsNullOrWhiteSpace(slot.OutputTemplate.OutputProvider)
                    ? outputProvider
                    : slot.OutputTemplate.OutputProvider;
                logger.LogInformation(
                    "Slot runtime: slot {SlotId} using output provider '{Provider}' ({Source}).",
                    slot.Id, slotOutputProvider,
                    string.IsNullOrWhiteSpace(slot.OutputTemplate.OutputProvider) ? "inherited from profile" : "set on slot");

                var pipeline = new ControllerMappingPipeline(slotProfile);
                var sink = outputSinkFactory.Create(slotOutputProvider);
                if (sink is IConfigurableOutputSink configurable)
                {
                    configurable.Configure(slot.OutputTemplate);
                }

                pipelines.Add(new SlotPipeline(slot.Id, slot.InputDeviceIds, slot.OutputTemplate, pipeline, sink));
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to build pipeline for slot {SlotId} ({SlotName}).", slot.Id, slot.Name);
            }
        }

        rebuildSw.Stop();
        logger.LogInformation(
            "Slot runtime built {Count} slot pipeline(s) in {ElapsedMs} ms.",
            pipelines.Count, rebuildSw.ElapsedMilliseconds);
        snapshotStore.Retain(pipelines.Select(p => p.SlotId));
    }

    /// <summary>
    /// Pumps the input source once, then ticks every slot pipeline.
    /// Returns the first slot's result (input/output display names +
    /// frame) so the caller can keep the live-view snapshot updated, or
    /// null if nothing was processed.
    /// </summary>
    public async ValueTask<(string Input, string Output, ControllerFrameResult Result)?> TickAsync(
        IMultiDeviceInputSource input, DateTimeOffset now, CancellationToken cancellationToken)
    {
        input.PumpForSlots();

        (string, string, ControllerFrameResult)? first = null;

        foreach (var pipeline in pipelines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var deviceIds = pipeline.InputDeviceIds;
                ControllerSnapshot snapshot;
                if (deviceIds.Count == 0)
                {
                    snapshot = ControllerSnapshot.Empty("No device assigned") with { Timestamp = now };
                }
                else if (deviceIds.Count == 1)
                {
                    snapshot = input.ReadDevice(deviceIds[0]);
                }
                else
                {
                    var snaps = new List<ControllerSnapshot>(deviceIds.Count);
                    foreach (var id in deviceIds)
                    {
                        snaps.Add(input.ReadDevice(id));
                    }
                    snapshot = ControllerSnapshotMerger.Merge("Merged", snaps) with { Timestamp = now };
                }

                var result = await pipeline.ProcessAsync(snapshot, now, cancellationToken);
                snapshotStore.Update(pipeline.SlotId, snapshot, result.VirtualSnapshot);
                // Lightbar removed: no per-tick SDL LED write to the physical
                // device (it could block the runtime loop on Bluetooth pads).
                first ??= ("Slot input", pipeline.OutputDisplayName, result);

                if (slotFailureCounts.TryGetValue(pipeline.SlotId, out var fails) && fails > 0)
                {
                    logger.LogInformation("Slot {SlotId} recovered after {Count} failure(s).", pipeline.SlotId, fails);
                    slotFailureCounts[pipeline.SlotId] = 0;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                int fails = slotFailureCounts.TryGetValue(pipeline.SlotId, out var prior) ? prior + 1 : 1;
                slotFailureCounts[pipeline.SlotId] = fails;
                if (fails == 1 || fails % 100 == 0)
                {
                    logger.LogWarning(exception, "Slot {SlotId} tick failed (failure #{Count}); other slots continue.", pipeline.SlotId, fails);
                }
            }
        }

        return first;
    }

    /// <summary>
    /// Loads and composes the slot's layered profiles. Missing ids are
    /// skipped; an empty/all-missing set yields the neutral empty profile
    /// (no remapping) — slots are independent of the global active profile.
    /// </summary>
    private async Task<ProfileDocument> ResolveSlotProfileAsync(ControllerSlot slot)
    {
        if (slot.ProfileIds.Count == 0)
        {
            return SlotProfileComposer.Empty;
        }

        var layers = new List<ProfileDocument>(slot.ProfileIds.Count);
        foreach (var profileId in slot.ProfileIds)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            logger.LogInformation(
                "Slot runtime: loading profile {ProfileId} for slot {SlotId}...",
                profileId, slot.Id);
            try
            {
                var doc = await profileRepository.LoadAsync(profileId);
                sw.Stop();
                logger.LogInformation(
                    "Slot runtime: profile {ProfileId} loaded for slot {SlotId} in {ElapsedMs} ms (found={Found}).",
                    profileId, slot.Id, sw.ElapsedMilliseconds, doc is not null);
                if (doc is not null)
                {
                    layers.Add(doc);
                }
                else
                {
                    logger.LogWarning("Slot {SlotId}: profile '{ProfileId}' not found; skipping layer.", slot.Id, profileId);
                }
            }
            catch (Exception exception)
            {
                sw.Stop();
                logger.LogWarning(exception,
                    "Slot {SlotId}: failed to load profile '{ProfileId}' after {ElapsedMs} ms.",
                    slot.Id, profileId, sw.ElapsedMilliseconds);
            }
        }

        return SlotProfileComposer.Compose(layers);
    }

    private async Task DisposePipelinesAsync()
    {
        foreach (var pipeline in pipelines)
        {
            try
            {
                await pipeline.DisposeAsync();
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "Error disposing slot pipeline {SlotId}.", pipeline.SlotId);
            }
        }
        pipelines.Clear();
        slotFailureCounts.Clear();
    }

    public async ValueTask DisposeAsync() => await DisposePipelinesAsync();
}
