using Autofire.Core.Models;

namespace Autofire.Core.Pipeline;

/// <summary>
/// Holds a stick vector that was captured at the moment of activation button press.
/// Issue #12: The latch now stores whatever the stick was doing the instant the button
/// was pressed (including zero / idle), rather than the last-ever non-zero position.
/// </summary>
public sealed class FreezeLatch
{
    public StickVector Current { get; private set; } = StickVector.Zero;

    /// <summary>
    /// Captures the provided vector unconditionally. Called once per rising edge of
    /// the activation button so that the frozen value always reflects the stick state
    /// at the exact moment of button press, not some historical non-zero position.
    /// </summary>
    public void CaptureSnapshot(StickVector value)
    {
        Current = value;
    }

    public void Clear()
    {
        Current = StickVector.Zero;
    }
}
