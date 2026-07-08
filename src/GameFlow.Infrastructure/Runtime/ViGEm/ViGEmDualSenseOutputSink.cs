using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using Microsoft.Extensions.Logging;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.DualShock4;

namespace GameFlow.Infrastructure.Runtime.ViGEm;

/// <summary>
/// Output sink that feeds a ViGEm Bus virtual DualSense (PS5) controller.
///
/// ViGEm Bus currently only exposes Xbox 360 and DualShock 4 target types natively.
/// DualSense (DS5) output is achieved by emitting as a DualShock 4 device and relying
/// on Steam / system remapping to present DS5 features where supported.  When the
/// Nefarius ViGEm client library gains a first-class DualSense target type this class
/// should be updated to use it.
///
/// Requires the ViGEm Bus driver v1.22+ installed on the host system:
/// https://github.com/nefarius/ViGEmBus/releases
/// </summary>
public sealed class ViGEmDualSenseOutputSink : IOutputSink
{
    private readonly ViGEmClient client;
    private readonly IDualShock4Controller controller;
    private readonly ILogger<ViGEmDualSenseOutputSink> logger;
    private bool disposed;

    public ViGEmDualSenseOutputSink(ILogger<ViGEmDualSenseOutputSink> logger)
    {
        this.logger = logger;

        client = new ViGEmClient();
        controller = client.CreateDualShock4Controller();
        controller.AutoSubmitReport = false;
        controller.Connect();

        logger.LogInformation("ViGEm DualSense (DS5/DS4-compat) virtual controller connected.");
    }

    public string DisplayName => "ViGEm DualSense";

    /// <summary>
    /// VID/PID of the device that ViGEm Bus actually emits when this sink
    /// is active. ViGEm has no native DualSense target, so under the hood
    /// the sink creates a DualShock 4 controller (Sony VID 0x054C,
    /// PID 0x09CC) and Windows enumerates the virtual device as a DS4 —
    /// not a DS5. The catalog filter that hides the active output device
    /// from the input source dropdown matches on this signature, so it
    /// must reflect what's actually on the bus, not the DS5 hardware
    /// that the sink is conceptually emulating.
    /// </summary>
    /// <remarks>
    /// Was previously set to (0x054C, 0x0CE6), the real DualSense PID,
    /// which never matched the emitted device — that is what caused the
    /// "DS5 output appears as a usable input source" / recursive-loop
    /// bug. When a future Nefarius.ViGEm.Client release ships a native
    /// DualSense target, switch this back to (0x054C, 0x0CE6).
    /// </remarks>
    public (ushort Vid, ushort Pid)? OwnedHardwareSignature => (0x054C, 0x09CC);

    public ValueTask WriteAsync(ControllerSnapshot snapshot, CancellationToken cancellationToken)
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        controller.SetAxisValue(DualShock4Axis.LeftThumbX,  NormalizeToDs4Axis(snapshot.LeftStick.X));
        controller.SetAxisValue(DualShock4Axis.LeftThumbY,  NormalizeToDs4AxisY(snapshot.LeftStick.Y));
        controller.SetAxisValue(DualShock4Axis.RightThumbX, NormalizeToDs4Axis(snapshot.RightStick.X));
        controller.SetAxisValue(DualShock4Axis.RightThumbY, NormalizeToDs4AxisY(snapshot.RightStick.Y));

        controller.SetSliderValue(DualShock4Slider.LeftTrigger,  NormalizeTrigger(snapshot.LeftTrigger));
        controller.SetSliderValue(DualShock4Slider.RightTrigger, NormalizeTrigger(snapshot.RightTrigger));

        controller.SetButtonState(DualShock4Button.Cross,        snapshot.IsPressed(ButtonId.South));
        controller.SetButtonState(DualShock4Button.Circle,       snapshot.IsPressed(ButtonId.East));
        controller.SetButtonState(DualShock4Button.Square,       snapshot.IsPressed(ButtonId.West));
        controller.SetButtonState(DualShock4Button.Triangle,     snapshot.IsPressed(ButtonId.North));
        controller.SetButtonState(DualShock4Button.ShoulderLeft, snapshot.IsPressed(ButtonId.LeftShoulder));
        controller.SetButtonState(DualShock4Button.ShoulderRight,snapshot.IsPressed(ButtonId.RightShoulder));
        controller.SetButtonState(DualShock4Button.TriggerLeft,  snapshot.IsPressed(ButtonId.LeftTriggerButton));
        controller.SetButtonState(DualShock4Button.TriggerRight, snapshot.IsPressed(ButtonId.RightTriggerButton));
        controller.SetButtonState(DualShock4Button.Share,        snapshot.IsPressed(ButtonId.Back));
        controller.SetButtonState(DualShock4Button.Options,      snapshot.IsPressed(ButtonId.Start));
        controller.SetButtonState(DualShock4SpecialButton.Ps,    snapshot.IsPressed(ButtonId.Guide));
        controller.SetButtonState(DualShock4Button.ThumbLeft,    snapshot.IsPressed(ButtonId.LeftStick));
        controller.SetButtonState(DualShock4Button.ThumbRight,   snapshot.IsPressed(ButtonId.RightStick));
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
        try { client.Dispose(); }        catch { /* best effort */ }

        logger.LogInformation("ViGEm DualSense virtual controller disconnected.");
        return ValueTask.CompletedTask;
    }

    private static byte NormalizeToDs4Axis(float value)
    {
        return (byte)Math.Clamp((int)((value + 1f) * 127.5f), 0, 255);
    }

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
        var up    = snapshot.IsPressed(ButtonId.DpadUp);
        var down  = snapshot.IsPressed(ButtonId.DpadDown);
        var left  = snapshot.IsPressed(ButtonId.DpadLeft);
        var right = snapshot.IsPressed(ButtonId.DpadRight);

        return (up, down, left, right) switch
        {
            (true,  false, false, false) => DualShock4DPadDirection.North,
            (true,  false, false, true)  => DualShock4DPadDirection.Northeast,
            (false, false, false, true)  => DualShock4DPadDirection.East,
            (false, true,  false, true)  => DualShock4DPadDirection.Southeast,
            (false, true,  false, false) => DualShock4DPadDirection.South,
            (false, true,  true,  false) => DualShock4DPadDirection.Southwest,
            (false, false, true,  false) => DualShock4DPadDirection.West,
            (true,  false, true,  false) => DualShock4DPadDirection.Northwest,
            _                            => DualShock4DPadDirection.None,
        };
    }
}
