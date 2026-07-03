namespace Autofire.Infrastructure.Runtime.Input;

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
}

/// <summary>No-op default: every keyboard reports an empty key set.</summary>
public sealed class NullKeyboardStateSource : IKeyboardStateSource
{
    private static readonly IReadOnlySet<int> Empty = new HashSet<int>();
    public IReadOnlySet<int> GetPressedKeys(string deviceId) => Empty;
}
