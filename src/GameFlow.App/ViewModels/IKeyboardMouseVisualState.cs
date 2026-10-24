using Avalonia;

namespace GameFlow.App.ViewModels;

/// <summary>
/// What the keyboard and mouse pictures (<c>KeyboardSurface</c>,
/// <c>MouseSurface</c>) bind to.
///
/// <para>
/// Two view models feed those pictures: the Devices page, which reads the
/// selected device directly, and every dashboard panel, which reads the
/// frames the runtime produced. The surfaces used to be typed to the
/// Devices page only, so on the dashboard every binding silently resolved
/// to nothing and the keyboard never lit up. One contract, two sources.
/// </para>
/// </summary>
public interface IKeyboardMouseVisualState
{
    string? KeyboardDeviceId => null;
    string? MouseDeviceId => null;
    void RequestInputMapping(int key = 0, GameFlow.Infrastructure.Runtime.Input.MouseInput mouse = GameFlow.Infrastructure.Runtime.Input.MouseInput.None) { }
    /// <summary>Virtual-key codes currently held.</summary>
    IReadOnlySet<int> PressedKeysSet { get; }

    bool IsMouseLeftDown { get; }
    bool IsMouseRightDown { get; }
    bool IsMouseMiddleDown { get; }
    bool IsMouseButton4Down { get; }
    bool IsMouseButton5Down { get; }

    /// <summary>The wheel turned up within the last moment.</summary>
    bool IsMouseScrollUp { get; }

    /// <summary>The wheel turned down within the last moment.</summary>
    bool IsMouseScrollDown { get; }

    /// <summary>End of the movement arrow, in the 60×60 canvas the mouse picture draws it in.</summary>
    Point MouseAimEndPoint { get; }
}
