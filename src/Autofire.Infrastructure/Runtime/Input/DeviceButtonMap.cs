using Autofire.Core.Enums;

namespace Autofire.Infrastructure.Runtime.Input;

/// <summary>
/// A per-physical-device button remap: maps a canonical
/// <see cref="ButtonId"/> to the raw joystick button index that actually
/// produces it on this device. Used to normalize controllers the OS/SDL
/// recognizes with a different button order. Unmapped buttons fall back to
/// the default (SDL gamepad mapping or the tentative joystick order).
/// </summary>
public sealed class DeviceButtonMap
{
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>Canonical button → raw joystick button index.</summary>
    public Dictionary<ButtonId, int> Buttons { get; set; } = new();

    public DeviceButtonMap Clone() => new()
    {
        DeviceId = DeviceId,
        Buttons = new Dictionary<ButtonId, int>(Buttons),
    };
}
