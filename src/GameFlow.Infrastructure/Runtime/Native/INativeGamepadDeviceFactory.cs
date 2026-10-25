namespace GameFlow.Infrastructure.Runtime.Native;

/// <summary>Creates one native gamepad per output slot.</summary>
public interface INativeGamepadDeviceFactory
{
    /// <summary>Creates a device or reports the missing operating-system capability.</summary>
    INativeGamepadDevice Create();
}
