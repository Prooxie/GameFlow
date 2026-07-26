using GameFlow.Infrastructure.Runtime;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Input.Linux;

/// <summary>
/// Linux counterpart to RawInputDeviceScanner (GameFlow.Infrastructure.Runtime.RawInput):
/// publishes evdev keyboards/mice to the InputDeviceCatalog so they
/// appear in the Devices view, using the exact same
/// EvdevDeviceDiscovery.BuildDeviceId scheme <see cref="LinuxRawInputReader"/>
/// keys its captured state under.
/// </summary>
internal static class LinuxInputDeviceScanner
{
    public static IReadOnlyList<InputDeviceInfo> Scan(ILogger? logger = null)
    {
        var devices = EvdevDeviceDiscovery.Scan(logger);
        var results = new List<InputDeviceInfo>(devices.Count * 2);

        foreach (var device in devices)
        {
            if (device.IsKeyboard)
            {
                results.Add(new InputDeviceInfo(
                    Id: EvdevDeviceDiscovery.BuildDeviceId("keyboard", device.Path),
                    DisplayName: device.Name,
                    Category: DeviceCategory.Keyboard));
            }
            if (device.IsMouse)
            {
                results.Add(new InputDeviceInfo(
                    Id: EvdevDeviceDiscovery.BuildDeviceId("mouse", device.Path),
                    DisplayName: device.Name,
                    Category: DeviceCategory.Mouse));
            }
        }

        return results;
    }
}
