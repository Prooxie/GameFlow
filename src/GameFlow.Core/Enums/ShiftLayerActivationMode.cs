namespace GameFlow.Core.Enums;

/// <summary>
/// How a <see cref="Models.ShiftLayer"/> engages. See
/// <see cref="Pipeline.ShiftLayerResolver"/> for the per-mode state
/// machine.
/// </summary>
public enum ShiftLayerActivationMode
{
    /// <summary>Active only while <see cref="Models.ShiftLayer.ActivatorButton"/> is physically held. Released → Base immediately.</summary>
    Hold,

    /// <summary>Press to turn on, press again to turn off (back to Base). Supports HoldToFireMs (tap passes through, hold flips) and AutoCancelMs (idle timeout reverts to Base).</summary>
    Toggle,

    /// <summary>Press to turn on and stay on. Turns off by pressing the SAME activator again, or by pressing a DIFFERENT Latch layer's activator (direct switch between latched layers, no detour through Base).</summary>
    Latch,

    /// <summary>Steps forward through an ordered queue of other layers (<see cref="Models.ShiftLayer.CycleLayerIds"/>); a second button steps back. This entry does not itself gate any rule — see <see cref="Models.ShiftLayer.CycleLayerIds"/>.</summary>
    Cycle,

    /// <summary>Press once to engage; stays active for exactly the next button press anywhere, then reverts to Base automatically — classic "sticky keys" behavior.</summary>
    Sticky,

    /// <summary>Has no activator of its own. Can only become active by appearing in another layer's Cycle queue.</summary>
    NoButton
}
