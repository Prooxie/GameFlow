using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Slots;

/// <summary>
/// Session-scoped registry of physical devices the user pinned to the
/// dashboard as LAYOUT-ONLY panels (no virtual half, no slot) — the
/// "show me this controller's layout and connections without creating a
/// virtual controller" affordance. The UI toggles pins from the sidebar;
/// the runtime coordinator reads each pinned device every tick and
/// publishes its snapshot here; the dashboard renders one physical-only
/// panel per pinned, connected device.
///
/// <para>Deliberately session-only for now (pins reset on restart):
/// persisting them belongs with the profile's UI preferences and can be
/// added without changing this surface.</para>
/// </summary>
public sealed class PhysicalPanelPinService
{
    private readonly Lock gate = new();
    private readonly HashSet<string> pinned = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ControllerSnapshot> snapshots = new(StringComparer.Ordinal);

    /// <summary>Raised on the toggling thread when the pin set changes.</summary>
    public event EventHandler? PinsChanged;

    public bool IsPinned(string deviceId)
    {
        lock (gate) { return pinned.Contains(deviceId); }
    }

    /// <summary>Toggles the pin and returns the new state.</summary>
    public bool TogglePin(string deviceId)
    {
        bool nowPinned;
        lock (gate)
        {
            if (!pinned.Add(deviceId))
            {
                pinned.Remove(deviceId);
                snapshots.Remove(deviceId);
                nowPinned = false;
            }
            else
            {
                nowPinned = true;
            }
        }
        PinsChanged?.Invoke(this, EventArgs.Empty);
        return nowPinned;
    }

    public IReadOnlyList<string> GetPinnedDeviceIds()
    {
        lock (gate) { return [.. pinned]; }
    }

    /// <summary>Latest snapshot published for a pinned device (empty placeholder until the first tick).</summary>
    public ControllerSnapshot GetSnapshot(string deviceId)
    {
        lock (gate)
        {
            return snapshots.TryGetValue(deviceId, out var snapshot)
                ? snapshot
                : ControllerSnapshot.Empty(deviceId);
        }
    }

    /// <summary>Called by the runtime coordinator once per tick per pinned device.</summary>
    public void PublishSnapshot(string deviceId, ControllerSnapshot snapshot)
    {
        lock (gate)
        {
            if (pinned.Contains(deviceId))
            {
                snapshots[deviceId] = snapshot;
            }
        }
    }
}
