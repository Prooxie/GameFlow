namespace Autofire.Infrastructure.Runtime;

/// <summary>Broad classification of an input device, used to group the Devices view by type.</summary>
public enum DeviceCategory
{
    Unknown = 0,
    Gamepad,
    Joystick,
    Keyboard,
    Mouse,
}

public sealed record InputDeviceInfo(
    string Id,
    string DisplayName,
    bool IsConnected = true,
    bool IsSelected = false,
    ushort VendorId = 0,
    ushort ProductId = 0,
    bool IsGamepad = false,
    DeviceCategory Category = DeviceCategory.Unknown)
{
    public string HardwareId => VendorId == 0 && ProductId == 0
        ? string.Empty
        : $"VID {VendorId:X4} · PID {ProductId:X4}";
}
