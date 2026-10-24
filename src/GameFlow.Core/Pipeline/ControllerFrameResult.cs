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

    /// <summary>
    /// What actually goes to the output sink. Identical to
    /// <see cref="VirtualSnapshot"/> unless an active
    /// <see cref="Models.Rules.AxisInvertRule"/> inverts axes without
    /// <see cref="Models.AxisInvert.ShowInLayout"/> — then the game gets
    /// the flip and the on-screen controller keeps drawing the direction
    /// the user pushed.
    ///
    /// <para>
    /// Defaulting to <see cref="VirtualSnapshot"/> rather than requiring
    /// a value keeps every existing construction site (and every test)
    /// correct by default: a caller that knows nothing about inversion
    /// writes the same snapshot it always did.
    /// </para>
    /// </summary>
    public ControllerSnapshot OutputSnapshot
    {
        get => field ?? VirtualSnapshot;
        init;
    }
}
