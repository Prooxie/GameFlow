using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using Microsoft.Extensions.Logging;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;
using Nefarius.ViGEm.Client.Targets.Xbox360;

namespace GameFlow.Infrastructure.Runtime.ViGEm;

/// <summary>
/// Output sink that feeds a ViGEm Bus virtual Xbox 360 controller.
/// Requires the ViGEm Bus driver to be installed on the host system.
/// https://github.com/nefarius/ViGEmBus/releases
/// </summary>
public sealed class ViGEmXbox360OutputSink : IOutputSink
{
    private readonly ViGEmClient client;
    private readonly IXbox360Controller controller;
    private readonly ILogger<ViGEmXbox360OutputSink> logger;
    private bool disposed;

    public ViGEmXbox360OutputSink(ILogger<ViGEmXbox360OutputSink> logger)
    {
        this.logger = logger;

        client = new ViGEmClient();
        controller = client.CreateXbox360Controller();
        controller.AutoSubmitReport = false;
        controller.Connect();

        logger.LogInformation("ViGEm Xbox 360 virtual controller connected.");
    }

    public string DisplayName => "ViGEm Xbox 360";

    /// <summary>
    /// VID/PID of the Microsoft Xbox 360 wired controller, which is what
    /// ViGEm Bus advertises this virtual device as. Used by the runtime to
    /// hide this sink from the input source dropdown while it is active.
    /// </summary>
    public (ushort Vid, ushort Pid)? OwnedHardwareSignature => (0x045E, 0x028E);

    public ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        controller.SetAxisValue(Xbox360Axis.LeftThumbX, NormalizeToXboxAxis(snapshot.LeftStick.X));
        controller.SetAxisValue(Xbox360Axis.LeftThumbY, NormalizeToXboxAxis(snapshot.LeftStick.Y));
        controller.SetAxisValue(Xbox360Axis.RightThumbX, NormalizeToXboxAxis(snapshot.RightStick.X));
        controller.SetAxisValue(Xbox360Axis.RightThumbY, NormalizeToXboxAxis(snapshot.RightStick.Y));

        controller.SetSliderValue(Xbox360Slider.LeftTrigger, NormalizeTrigger(snapshot.LeftTrigger));
        controller.SetSliderValue(Xbox360Slider.RightTrigger, NormalizeTrigger(snapshot.RightTrigger));

        controller.SetButtonState(Xbox360Button.A, snapshot.IsPressed(ButtonId.South));
        controller.SetButtonState(Xbox360Button.B, snapshot.IsPressed(ButtonId.East));
        controller.SetButtonState(Xbox360Button.X, snapshot.IsPressed(ButtonId.West));
        controller.SetButtonState(Xbox360Button.Y, snapshot.IsPressed(ButtonId.North));
        controller.SetButtonState(Xbox360Button.LeftShoulder, snapshot.IsPressed(ButtonId.LeftShoulder));
        controller.SetButtonState(Xbox360Button.RightShoulder, snapshot.IsPressed(ButtonId.RightShoulder));
        controller.SetButtonState(Xbox360Button.Back, snapshot.IsPressed(ButtonId.Back));
        controller.SetButtonState(Xbox360Button.Start, snapshot.IsPressed(ButtonId.Start));
        controller.SetButtonState(Xbox360Button.Guide, snapshot.IsPressed(ButtonId.Guide));
        controller.SetButtonState(Xbox360Button.LeftThumb, snapshot.IsPressed(ButtonId.LeftStick));
        controller.SetButtonState(Xbox360Button.RightThumb, snapshot.IsPressed(ButtonId.RightStick));
        controller.SetButtonState(Xbox360Button.Up, snapshot.IsPressed(ButtonId.DpadUp));
        controller.SetButtonState(Xbox360Button.Down, snapshot.IsPressed(ButtonId.DpadDown));
        controller.SetButtonState(Xbox360Button.Left, snapshot.IsPressed(ButtonId.DpadLeft));
        controller.SetButtonState(Xbox360Button.Right, snapshot.IsPressed(ButtonId.DpadRight));

        controller.SubmitReport();
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        disposed = true;

        try { controller.Disconnect(); } catch { /* best effort */ }
        try { client.Dispose(); } catch { /* best effort */ }

        logger.LogInformation("ViGEm Xbox 360 virtual controller disconnected.");
        return ValueTask.CompletedTask;
    }

    private static short NormalizeToXboxAxis(float value)
    {
        return (short)Math.Clamp((int)(value * 32767f), short.MinValue + 1, short.MaxValue);
    }

    private static byte NormalizeTrigger(float value)
    {
        return (byte)Math.Clamp((int)(value * 255f), 0, 255);
    }
}

/// <summary>
/// Output sink that feeds a ViGEm Bus virtual DualShock 4 controller.
/// Requires the ViGEm Bus driver to be installed on the host system.
/// https://github.com/nefarius/ViGEmBus/releases
/// </summary>
public sealed class ViGEmDualShock4OutputSink : IOutputSink
{
    private readonly ViGEmClient client;
    private readonly IDualShock4Controller controller;
    private readonly ILogger<ViGEmDualShock4OutputSink> logger;
    private bool disposed;

    public ViGEmDualShock4OutputSink(ILogger<ViGEmDualShock4OutputSink> logger)
    {
        this.logger = logger;

        client = new ViGEmClient();
        controller = client.CreateDualShock4Controller();
        controller.AutoSubmitReport = false;
        controller.Connect();

        logger.LogInformation("ViGEm DualShock 4 virtual controller connected.");
    }

    public string DisplayName => "ViGEm DualShock 4";

    /// <summary>
    /// VID/PID of the Sony DualShock 4 (wired CUH-ZCT2 revision), which is
    /// what ViGEm Bus advertises this virtual device as. Used by the runtime
    /// to hide this sink from the input source dropdown while it is active.
    /// </summary>
    public (ushort Vid, ushort Pid)? OwnedHardwareSignature => (0x054C, 0x09CC);

    public ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        controller.SetAxisValue(DualShock4Axis.LeftThumbX, NormalizeToDs4Axis(snapshot.LeftStick.X));
        controller.SetAxisValue(DualShock4Axis.LeftThumbY, NormalizeToDs4AxisY(snapshot.LeftStick.Y));
        controller.SetAxisValue(DualShock4Axis.RightThumbX, NormalizeToDs4Axis(snapshot.RightStick.X));
        controller.SetAxisValue(DualShock4Axis.RightThumbY, NormalizeToDs4AxisY(snapshot.RightStick.Y));

        controller.SetSliderValue(DualShock4Slider.LeftTrigger, NormalizeTrigger(snapshot.LeftTrigger));
        controller.SetSliderValue(DualShock4Slider.RightTrigger, NormalizeTrigger(snapshot.RightTrigger));

        controller.SetButtonState(DualShock4Button.Cross, snapshot.IsPressed(ButtonId.South));
        controller.SetButtonState(DualShock4Button.Circle, snapshot.IsPressed(ButtonId.East));
        controller.SetButtonState(DualShock4Button.Square, snapshot.IsPressed(ButtonId.West));
        controller.SetButtonState(DualShock4Button.Triangle, snapshot.IsPressed(ButtonId.North));
        controller.SetButtonState(DualShock4Button.ShoulderLeft, snapshot.IsPressed(ButtonId.LeftShoulder));
        controller.SetButtonState(DualShock4Button.ShoulderRight, snapshot.IsPressed(ButtonId.RightShoulder));
        controller.SetButtonState(DualShock4Button.TriggerLeft, snapshot.IsPressed(ButtonId.LeftTriggerButton));
        controller.SetButtonState(DualShock4Button.TriggerRight, snapshot.IsPressed(ButtonId.RightTriggerButton));
        controller.SetButtonState(DualShock4Button.Share, snapshot.IsPressed(ButtonId.Back));
        controller.SetButtonState(DualShock4Button.Options, snapshot.IsPressed(ButtonId.Start));
        controller.SetButtonState(DualShock4SpecialButton.Ps, snapshot.IsPressed(ButtonId.Guide));
        controller.SetButtonState(DualShock4Button.ThumbLeft, snapshot.IsPressed(ButtonId.LeftStick));
        controller.SetButtonState(DualShock4Button.ThumbRight, snapshot.IsPressed(ButtonId.RightStick));
        controller.SetButtonState(DualShock4SpecialButton.Touchpad, snapshot.IsPressed(ButtonId.Touchpad));

        controller.SetDPadDirection(MapDpad(snapshot));
        controller.SubmitReport();

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        disposed = true;

        try { controller.Disconnect(); } catch { /* best effort */ }
        try { client.Dispose(); } catch { /* best effort */ }

        logger.LogInformation("ViGEm DualShock 4 virtual controller disconnected.");
        return ValueTask.CompletedTask;
    }

    private static byte NormalizeToDs4Axis(float value)
    {
        return (byte)Math.Clamp((int)((value + 1f) * 127.5f), 0, 255);
    }

    // DS4 Y axis is inverted relative to our model: 0=up, 255=down
    private static byte NormalizeToDs4AxisY(float value)
    {
        return NormalizeToDs4Axis(-value);
    }

    private static byte NormalizeTrigger(float value)
    {
        return (byte)Math.Clamp((int)(value * 255f), 0, 255);
    }

    private static DualShock4DPadDirection MapDpad(ControllerSnapshot snapshot)
    {
        var up = snapshot.IsPressed(ButtonId.DpadUp);
        var down = snapshot.IsPressed(ButtonId.DpadDown);
        var left = snapshot.IsPressed(ButtonId.DpadLeft);
        var right = snapshot.IsPressed(ButtonId.DpadRight);

        return (up, down, left, right) switch
        {
            (true, false, false, false) => DualShock4DPadDirection.North,
            (true, false, false, true) => DualShock4DPadDirection.Northeast,
            (false, false, false, true) => DualShock4DPadDirection.East,
            (false, true, false, true) => DualShock4DPadDirection.Southeast,
            (false, true, false, false) => DualShock4DPadDirection.South,
            (false, true, true, false) => DualShock4DPadDirection.Southwest,
            (false, false, true, false) => DualShock4DPadDirection.West,
            (true, false, true, false) => DualShock4DPadDirection.Northwest,
            _ => DualShock4DPadDirection.None,
        };
    }
}
