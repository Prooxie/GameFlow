namespace Autofire.Infrastructure.Runtime;

/// <summary>
/// A raw, physical-layer snapshot of one input device, produced by the
/// SDL source for the Devices view's currently-selected device. Unlike
/// <see cref="Autofire.Core.Models.ControllerSnapshot"/> (which is the
/// gamepad-mapped, pipeline-facing state of the *active* device), this
/// is the raw joystick view — exactly the axis/button/hat indices the
/// hardware reports, with no remapping. Intended purely for the
/// device-inspection UI.
/// </summary>
/// <param name="DeviceId">Catalog id of the inspected device.</param>
/// <param name="Axes">
/// Raw axis values in SDL's signed 16-bit range (-32768..32767), in
/// hardware axis-index order.
/// </param>
/// <param name="Buttons">Button pressed-states in hardware button-index order.</param>
/// <param name="Hats">
/// SDL hat bitmasks (SDL_HAT_* — bit 0 up, 1 right, 2 down, 3 left) in
/// hardware hat-index order.
/// </param>
public sealed record RawDeviceSnapshot(
    string DeviceId,
    IReadOnlyList<short> Axes,
    IReadOnlyList<bool> Buttons,
    IReadOnlyList<byte> Hats);
