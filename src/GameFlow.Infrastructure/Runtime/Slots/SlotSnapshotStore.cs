using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Slots;

/// <summary>
/// One slot's most recent input/output pair: <see cref="Physical"/> is the
/// raw snapshot read from the slot's assigned input device(s), before
/// mapping; <see cref="Virtual"/> is what the pipeline produced and wrote
/// to the output sink. Keeping both (not just the virtual side) is what
/// lets the dashboard show a real physical-vs-virtual comparison per slot
/// instead of only the output.
/// </summary>
public readonly record struct SlotSnapshotPair(ControllerSnapshot Physical, ControllerSnapshot Virtual);

/// <summary>
/// Holds the most recent physical/virtual snapshot pair for each slot,
/// written by <see cref="SlotRuntime"/> every tick and read by the
/// dashboard's per-controller panels. Like <see cref="RuntimeSnapshotStore"/>
/// but keyed by slot id, so each running slot has its own live state.
/// </summary>
public sealed class SlotSnapshotStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, SlotSnapshotPair> snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> outputStatusById = new(StringComparer.Ordinal);

    /// <summary>
    /// Records the slot sink's human-readable status (its DisplayName —
    /// e.g. "HIDMaestro unavailable — no output (needs Administrator)").
    /// The dashboard shows it on the virtual panel so "why is there no
    /// output" is answered ON SCREEN instead of only in the log.
    /// </summary>
    public void SetOutputStatus(string slotId, string status)
    {
        if (string.IsNullOrEmpty(slotId))
        {
            return;
        }
        lock (gate)
        {
            outputStatusById[slotId] = status ?? string.Empty;
        }
    }

    /// <summary>The slot sink's latest status text, or empty when unknown.</summary>
    public string GetOutputStatus(string slotId)
    {
        lock (gate)
        {
            return outputStatusById.TryGetValue(slotId, out var status) ? status : string.Empty;
        }
    }

    /// <summary>Records a slot's latest physical (pre-mapping) and virtual (post-mapping) snapshots.</summary>
    public void Update(string slotId, ControllerSnapshot physical, ControllerSnapshot virtualSnapshot)
    {
        if (string.IsNullOrEmpty(slotId))
        {
            return;
        }
        lock (gate)
        {
            snapshots[slotId] = new SlotSnapshotPair(physical, virtualSnapshot);
        }
    }

    /// <summary>Returns the slot's latest snapshot pair, or empty placeholders if none yet.</summary>
    public SlotSnapshotPair Get(string slotId)
    {
        lock (gate)
        {
            return snapshots.TryGetValue(slotId, out var pair)
                ? pair
                : new SlotSnapshotPair(
                    ControllerSnapshot.Empty("Waiting for slot"),
                    ControllerSnapshot.Empty("Waiting for slot"));
        }
    }

    /// <summary>Drops snapshots for slots no longer present.</summary>
    public void Retain(IEnumerable<string> liveSlotIds)
    {
        var live = new HashSet<string>(liveSlotIds, StringComparer.Ordinal);
        lock (gate)
        {
            foreach (var id in snapshots.Keys.Where(id => !live.Contains(id)).ToList())
            {
                _ = snapshots.Remove(id);
                _ = outputStatusById.Remove(id);
            }
        }
    }

    /// <summary>Clears all slot snapshots.</summary>
    public void Clear()
    {
        lock (gate)
        {
            snapshots.Clear();
        }
    }
}
