using GameFlow.Core.Enums;
using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>One rumble command queued back to a phone, played via the browser Vibration API.</summary>
public readonly record struct WebRumbleCommand(float LowFrequency, float HighFrequency, int DurationMs);

/// <summary>
/// Shared state between <see cref="WebControllerServer"/> (which receives
/// phone input over WebSocket) and <see cref="WebControllerInputSource"/>
/// (which hands it to the mapping pipeline as an ordinary snapshot).
///
/// <para>
/// Each connected phone owns one pad index and becomes one independent
/// virtual controller, up to <see cref="MaxPads"/>. All access is under
/// a single lock: the write side is one socket-receive task per phone,
/// the read side is the runtime tick, and both are frequent but tiny —
/// contention isn't a concern at this granularity, and the simplicity
/// is worth more than shaving a lock.
/// </para>
/// </summary>
public sealed class WebControllerHub
{
    /// <summary>Matches the roadmap's "up to 16 phones at once", and the runtime's own 16-slot ceiling.</summary>
    public const int MaxPads = 16;

    private readonly Lock gate = new();
    private readonly ControllerSnapshot?[] pads = new ControllerSnapshot?[MaxPads];
    private readonly Queue<WebRumbleCommand>[] rumbleQueues = new Queue<WebRumbleCommand>[MaxPads];
    private readonly DateTimeOffset[] lastSeen = new DateTimeOffset[MaxPads];

    /// <summary>A pad with no traffic for this long is treated as gone — covers a phone that walks out of Wi-Fi range without closing its socket.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(5);

    public WebControllerHub()
    {
        for (var i = 0; i < MaxPads; i++)
        {
            rumbleQueues[i] = new Queue<WebRumbleCommand>();
        }
    }

    /// <summary>Claims the lowest free pad index, or -1 when all <see cref="MaxPads"/> are taken.</summary>
    public int ClaimPad()
    {
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            for (var i = 0; i < MaxPads; i++)
            {
                if (pads[i] is null || now - lastSeen[i] > StaleAfter)
                {
                    pads[i] = BuildNeutralSnapshot(i);
                    lastSeen[i] = now;
                    rumbleQueues[i].Clear();
                    return i;
                }
            }
            return -1;
        }
    }

    public void ReleasePad(int padIndex)
    {
        if (!IsValid(padIndex))
        {
            return;
        }
        lock (gate)
        {
            pads[padIndex] = null;
            rumbleQueues[padIndex].Clear();
        }
    }

    public void UpdatePad(int padIndex, ControllerSnapshot snapshot)
    {
        if (!IsValid(padIndex))
        {
            return;
        }
        lock (gate)
        {
            pads[padIndex] = snapshot;
            lastSeen[padIndex] = DateTimeOffset.UtcNow;
        }
    }

    /// <summary>
    /// Latest input for a pad. Returns a NEUTRAL snapshot (not the last
    /// one received) when the pad is disconnected or has gone stale —
    /// otherwise a phone that dies mid-press would leave that button
    /// held down in the game forever.
    /// </summary>
    public ControllerSnapshot GetSnapshot(int padIndex)
    {
        if (!IsValid(padIndex))
        {
            return BuildNeutralSnapshot(padIndex);
        }
        lock (gate)
        {
            var snapshot = pads[padIndex];
            if (snapshot is null || DateTimeOffset.UtcNow - lastSeen[padIndex] > StaleAfter)
            {
                return BuildNeutralSnapshot(padIndex);
            }
            // ControllerSnapshot is `record` (reference type), so the `?`
            // above is a nullable-REFERENCE annotation, not Nullable<T> —
            // there's no .Value to unwrap. After the null check, `snapshot`
            // is already the non-null value.
            return snapshot;
        }
    }

    public bool IsPadConnected(int padIndex)
    {
        if (!IsValid(padIndex))
        {
            return false;
        }
        lock (gate)
        {
            return pads[padIndex] is not null && DateTimeOffset.UtcNow - lastSeen[padIndex] <= StaleAfter;
        }
    }

    public IReadOnlyList<int> GetConnectedPads()
    {
        lock (gate)
        {
            var now = DateTimeOffset.UtcNow;
            var result = new List<int>();
            for (var i = 0; i < MaxPads; i++)
            {
                if (pads[i] is not null && now - lastSeen[i] <= StaleAfter)
                {
                    result.Add(i);
                }
            }
            return result;
        }
    }

    /// <summary>Queues rumble for a phone. Bounded so a phone that stops draining its queue can't grow it without limit.</summary>
    public void QueueRumble(int padIndex, WebRumbleCommand command)
    {
        if (!IsValid(padIndex))
        {
            return;
        }
        lock (gate)
        {
            var queue = rumbleQueues[padIndex];
            if (queue.Count >= 8)
            {
                _ = queue.Dequeue(); // drop the oldest — a stale buzz is worth less than the newest one
            }
            queue.Enqueue(command);
        }
    }

    public bool TryDequeueRumble(int padIndex, out WebRumbleCommand command)
    {
        command = default;
        if (!IsValid(padIndex))
        {
            return false;
        }
        lock (gate)
        {
            return rumbleQueues[padIndex].TryDequeue(out command);
        }
    }

    private static bool IsValid(int padIndex) => padIndex >= 0 && padIndex < MaxPads;

    private static ControllerSnapshot BuildNeutralSnapshot(int padIndex) => new()
    {
        DeviceName = $"Web Controller #{padIndex + 1}",
        Buttons = ButtonState.CreateEmptyMap(),
        Timestamp = DateTimeOffset.UtcNow
    };
}
