namespace Autofire.Infrastructure.Runtime;

public sealed record InputDeviceInfo(
    string Id,
    string DisplayName,
    bool IsConnected = true,
    bool IsSelected = false,
    ushort VendorId = 0,
    ushort ProductId = 0)
{
    public string HardwareId => VendorId == 0 && ProductId == 0
        ? string.Empty
        : $"VID {VendorId:X4} · PID {ProductId:X4}";
}
