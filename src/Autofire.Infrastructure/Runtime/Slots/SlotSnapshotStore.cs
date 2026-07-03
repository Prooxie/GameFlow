using Autofire.Core.Models;

namespace Autofire.Infrastructure.Runtime.Slots;

/// <summary>
/// Holds the most recent virtual <see cref="ControllerSnapshot"/> for each
/// slot, written by <see cref="SlotRuntime"/> every tick and read by the
/// dashboard's per-controller panels. Like <see cref="RuntimeSnapshotStore"/>
/// but keyed by slot id, so each running slot has its own live state.
/// </summary>
public sealed class SlotSnapshotStore
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, ControllerSnapshot> snapshots = new(StringComparer.Ordinal);

    /// <summary>Records a slot's latest virtual snapshot.</summary>
    public void Update(string slotId, ControllerSnapshot snapshot)
    {
        if (string.IsNullOrEmpty(slotId))
        {
            return;
        }
        lock (gate)
        {
            snapshots[slotId] = snapshot;
        }
    }

    /// <summary>Returns the slot's latest snapshot, or an empty one if none yet.</summary>
    public ControllerSnapshot Get(string slotId)
    {
        lock (gate)
        {
            return snapshots.TryGetValue(slotId, out var snapshot)
                ? snapshot
                : ControllerSnapshot.Empty("Waiting for slot");
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
