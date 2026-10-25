namespace GameFlow.Infrastructure.Runtime.Native;

/// <summary>Selects the native gamepad transport for Linux or macOS.</summary>
public sealed class NativeGamepadDeviceFactory : INativeGamepadDeviceFactory
{
    /// <inheritdoc />
    public INativeGamepadDevice Create()
    {
        if (OperatingSystem.IsLinux()) { return new LinuxGamepadDevice(); }
        if (OperatingSystem.IsMacOS()) { return new MacGamepadDevice(); }
        throw new PlatformNotSupportedException("Native gamepad output requires Linux or macOS.");
    }
}
