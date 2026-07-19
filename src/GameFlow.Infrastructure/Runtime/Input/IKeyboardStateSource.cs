namespace GameFlow.Infrastructure.Runtime.Input;

/// <summary>
/// Per-keyboard-device source of currently-pressed Windows virtual-key
/// codes. The Raw Input WM_INPUT reader will implement this; until then a
/// <see cref="NullKeyboardStateSource"/> returns an empty set so the
/// synthesizer + UI compile and the integration point is fixed.
/// </summary>
public interface IKeyboardStateSource
{
    /// <summary>Currently-pressed virtual-key codes for the given device id.</summary>
    IReadOnlySet<int> GetPressedKeys(string deviceId);

    /// <summary>
    /// Keys currently down across EVERY tracked keyboard. Windows often
    /// exposes one physical keyboard as several Raw Input handles
    /// (composite HID), so the id the user assigned isn't necessarily
    /// the one keystrokes arrive under — consumers use the per-device
    /// read first and fall back to this union when it's empty.
    /// </summary>
    IReadOnlySet<int> GetPressedKeysAggregate();
}

/// <summary>No-op default: every keyboard reports an empty key set.</summary>
public sealed class NullKeyboardStateSource : IKeyboardStateSource
{
    private static readonly IReadOnlySet<int> Empty = new HashSet<int>();
    public IReadOnlySet<int> GetPressedKeys(string deviceId) => Empty;
    public IReadOnlySet<int> GetPressedKeysAggregate() => Empty;
}
