using GameFlow.Core.Models;

namespace GameFlow.Core.Pipeline;

public sealed record ControllerFrameResult(
    ControllerSnapshot PhysicalSnapshot,
    ControllerSnapshot VirtualSnapshot,
    IReadOnlyList<string> Notes)
{
    /// <summary>Empty = Base. The layer <see cref="ShiftLayerResolver"/> resolved as active for this tick — a future flyout binds directly to this instead of parsing <see cref="Notes"/>.</summary>
    public string ActiveLayerId { get; init; } = string.Empty;

    /// <summary>
    /// This tick's relative mouse-cursor movement in screen pixels
    /// (e.g. from TouchpadMapRule's mouse mode), zero when nothing
    /// produced any. Unlike every other field on this snapshot, there is
    /// no persistent "current mouse position" state anywhere in this
    /// pipeline — a delta is consumed once by whatever calls the OS
    /// mouse-move API and then it's gone, the same way a physical
    /// mouse's movement packets work.
    /// </summary>
    public float MouseDeltaX { get; init; }
    public float MouseDeltaY { get; init; }
}
