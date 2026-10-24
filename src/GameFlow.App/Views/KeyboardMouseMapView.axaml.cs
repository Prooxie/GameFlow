using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using GameFlow.App.ViewModels;
using GameFlow.Infrastructure.Runtime.Input;

namespace GameFlow.App.Views;

/// <summary>The keyboard &amp; mouse controller map editor. See <see cref="KeyboardMouseMapEditorViewModel"/>.</summary>
public partial class KeyboardMouseMapView : UserControl
{
    /// <summary>Keys swallowed on the way down, whose release must be swallowed too.</summary>
    private readonly HashSet<Key> swallowedKeys = [];

    public KeyboardMouseMapView()
    {
        InitializeComponent();

        // While the editor is listening for a key, that key is an answer,
        // not a command. Without this, Space re-pressed the focused
        // "+ Bind" button on release (starting a new capture), Enter
        // pressed a default button and Escape closed the whole window
        // instead of cancelling the capture. Tunnel, so this runs before
        // any button or window handler sees the key.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnPreviewKeyUp, RoutingStrategies.Tunnel);
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is KeyboardMouseMapEditorViewModel { IsCapturing: true } model)
        {
            if (e.Key == Key.Escape) { model.CancelCaptureCommand.Execute(null); }
            swallowedKeys.Add(e.Key);
            e.Handled = true;
        }
    }

    private void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (swallowedKeys.Remove(e.Key))
        {
            e.Handled = true;
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>A click inside the capture box binds that mouse button.</summary>
    private void OnCapturePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not KeyboardMouseMapEditorViewModel { IsCapturing: true } viewModel)
        {
            return;
        }

        var input = e.GetCurrentPoint(sender as Control).Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed => MouseInput.Left,
            PointerUpdateKind.RightButtonPressed => MouseInput.Right,
            PointerUpdateKind.MiddleButtonPressed => MouseInput.Middle,
            PointerUpdateKind.XButton1Pressed => MouseInput.Back,
            PointerUpdateKind.XButton2Pressed => MouseInput.Forward,
            _ => MouseInput.None,
        };

        viewModel.CaptureMouse(input);
        e.Handled = true;
    }

    /// <summary>A wheel notch inside the capture box binds that wheel direction.</summary>
    private void OnCaptureWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not KeyboardMouseMapEditorViewModel { IsCapturing: true } viewModel || e.Delta.Y == 0)
        {
            return;
        }

        viewModel.CaptureMouse(e.Delta.Y > 0 ? MouseInput.WheelUp : MouseInput.WheelDown);
        e.Handled = true;
    }
}
