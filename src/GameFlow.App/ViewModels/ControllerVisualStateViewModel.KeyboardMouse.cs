using Avalonia;
using GameFlow.Core.Enums;
using GameFlow.Core.Models;

namespace GameFlow.App.ViewModels;

/// <summary>
/// Keyboard and mouse pictures for a dashboard panel, fed from the frames
/// the runtime produced rather than from the device directly — so a slot's
/// panel shows exactly what its keyboard and mouse sent into the pipeline.
/// </summary>
public sealed partial class ControllerVisualStateViewModel : IKeyboardMouseVisualState
{
    private static readonly IReadOnlySet<int> NoKeys = new HashSet<int>();

    private IReadOnlySet<int> pressedKeysSet = NoKeys;
    private MouseButtonFlags mouseButtons;
    private Point mouseAimEndPoint = new(30, 30);
    private string? keyboardDeviceId;
    private string? mouseDeviceId;
    public string? KeyboardDeviceId => keyboardDeviceId;
    public string? MouseDeviceId => mouseDeviceId;
    public event EventHandler<KeyboardMouseMapRequest>? InputMappingRequested;
    public void RequestInputMapping(int key = 0, GameFlow.Infrastructure.Runtime.Input.MouseInput mouse = GameFlow.Infrastructure.Runtime.Input.MouseInput.None) =>
        InputMappingRequested?.Invoke(this, new(key > 0 ? keyboardDeviceId : mouseDeviceId, key, mouse));

    public void SetInputDeviceIds(string? keyboard, string? mouse)
    {
        SetProperty(ref keyboardDeviceId, keyboard, nameof(KeyboardDeviceId));
        SetProperty(ref mouseDeviceId, mouse, nameof(MouseDeviceId));
    }

    /// <summary>Whether this panel draws the keyboard picture.</summary>
    public bool ShowKeyboardSurface =>
        visualStyle is ControllerVisualStyle.Keyboard or ControllerVisualStyle.KeyboardMouse;

    /// <summary>
    /// Whether this panel draws the mouse picture — the same one the
    /// Devices page shows. A mouse used to go through the theme engine's
    /// VSCView mouse pack, which looked nothing like the Devices page and,
    /// because a mouse frame carries no gamepad state that pack reads,
    /// never showed a click or a movement.
    /// </summary>
    public bool ShowMouseSurface =>
        visualStyle is ControllerVisualStyle.Mouse or ControllerVisualStyle.KeyboardMouse;

    public IReadOnlySet<int> PressedKeysSet => pressedKeysSet;

    public bool IsMouseLeftDown => (mouseButtons & MouseButtonFlags.Left) != 0;
    public bool IsMouseRightDown => (mouseButtons & MouseButtonFlags.Right) != 0;
    public bool IsMouseMiddleDown => (mouseButtons & MouseButtonFlags.Middle) != 0;
    public bool IsMouseButton4Down => (mouseButtons & MouseButtonFlags.Back) != 0;
    public bool IsMouseButton5Down => (mouseButtons & MouseButtonFlags.Forward) != 0;
    public bool IsMouseScrollUp => (mouseButtons & MouseButtonFlags.WheelUp) != 0;
    public bool IsMouseScrollDown => (mouseButtons & MouseButtonFlags.WheelDown) != 0;
    public Point MouseAimEndPoint => mouseAimEndPoint;

    /// <summary>
    /// Copies the keyboard and mouse fields of the current frame, raising
    /// only what changed — a keyboard panel at rest must not repaint every
    /// key thirty times a second.
    /// </summary>
    private void UpdateKeyboardMouseState(bool styleChanged)
    {
        if (styleChanged)
        {
            OnPropertyChanged(nameof(ShowKeyboardSurface));
            OnPropertyChanged(nameof(ShowMouseSurface));
        }

        if (!ShowKeyboardSurface && !ShowMouseSurface)
        {
            return;
        }

        var keys = snapshot.PressedKeys;
        if (!ReferenceEquals(keys, pressedKeysSet) && !keys.SetEquals(pressedKeysSet))
        {
            pressedKeysSet = keys.Count == 0 ? NoKeys : keys;
            OnPropertyChanged(nameof(PressedKeysSet));
        }

        if (snapshot.MouseButtons != mouseButtons)
        {
            var changed = snapshot.MouseButtons ^ mouseButtons;
            mouseButtons = snapshot.MouseButtons;
            if ((changed & MouseButtonFlags.Left) != 0) { OnPropertyChanged(nameof(IsMouseLeftDown)); }
            if ((changed & MouseButtonFlags.Right) != 0) { OnPropertyChanged(nameof(IsMouseRightDown)); }
            if ((changed & MouseButtonFlags.Middle) != 0) { OnPropertyChanged(nameof(IsMouseMiddleDown)); }
            if ((changed & MouseButtonFlags.Back) != 0) { OnPropertyChanged(nameof(IsMouseButton4Down)); }
            if ((changed & MouseButtonFlags.Forward) != 0) { OnPropertyChanged(nameof(IsMouseButton5Down)); }
            if ((changed & MouseButtonFlags.WheelUp) != 0) { OnPropertyChanged(nameof(IsMouseScrollUp)); }
            if ((changed & MouseButtonFlags.WheelDown) != 0) { OnPropertyChanged(nameof(IsMouseScrollDown)); }
        }

        var aim = AimEndPointFor(snapshot.MouseMotion);
        if (aim != mouseAimEndPoint)
        {
            mouseAimEndPoint = aim;
            OnPropertyChanged(nameof(MouseAimEndPoint));
        }
    }

    /// <summary>
    /// Maps a −1‥1 deflection (Y up) to the arrow end in the mouse
    /// picture's 60×60 canvas, centred at (30, 30) with a 25 px reach.
    /// </summary>
    internal static Point AimEndPointFor(StickVector motion)
    {
        var x = float.IsFinite(motion.X) ? Math.Clamp(motion.X, -1f, 1f) : 0;
        var y = float.IsFinite(motion.Y) ? Math.Clamp(motion.Y, -1f, 1f) : 0;
        return new Point(Math.Round(30 + (x * 25), 1), Math.Round(30 - (y * 25), 1));
    }
}
