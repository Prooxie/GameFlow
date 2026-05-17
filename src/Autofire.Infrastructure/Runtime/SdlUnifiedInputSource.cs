using System.Runtime.InteropServices;
using Autofire.Core.Enums;
using Autofire.Core.Models;
using Autofire.Infrastructure.Runtime.Sdl;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime;

public sealed class SdlUnifiedInputSource : IInputSource
{
    private readonly Lock syncRoot = new();
    private readonly ILogger<SdlUnifiedInputSource> logger;
    private readonly InputDeviceCatalog inputDeviceCatalog;
    private OpenedDevice? openedDevice;
    private bool disposed;
    private bool sdlInitialized;

    public SdlUnifiedInputSource(ILogger<SdlUnifiedInputSource> logger, InputDeviceCatalog inputDeviceCatalog)
    {
        this.logger = logger;
        this.inputDeviceCatalog = inputDeviceCatalog;

        SdlInterop.SetMainReady();
        _ = SdlInterop.SetHint(SdlInterop.HintJoystickThread, "1");
        _ = SdlInterop.SetHint(SdlInterop.HintJoystickAllowBackgroundEvents, "1");
        _ = SdlInterop.SetHint(SdlInterop.HintJoystickHidApi, "1");
        _ = SdlInterop.SetHint(SdlInterop.HintJoystickDirectInput, "1");
        _ = SdlInterop.SetHint(SdlInterop.HintXInputEnabled, "1");
        _ = SdlInterop.SetHint(SdlInterop.HintAutoUpdateJoysticks, "0");

        if (!SdlInterop.Init(SdlInterop.InitGamepad | SdlInterop.InitJoystick))
        {
            throw new InvalidOperationException($"SDL3 input initialization failed: {SdlInterop.GetError()}");
        }

        sdlInitialized = true;
        TryLoadOptionalMappings();
        RefreshDeviceCatalog();
        logger.LogInformation("Initialized SDL3 unified input provider.");
    }

    public string DisplayName => "SDL3 unified input";

    public ValueTask<ControllerSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            ThrowIfDisposed();
            SdlInterop.UpdateJoysticks();
            SdlInterop.UpdateGamepads();
            RefreshDeviceCatalog();
            return ValueTask.FromResult(ReadSnapshotCore());
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (syncRoot)
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
            CloseOpenedDevice();

            if (sdlInitialized)
            {
                try
                {
                    SdlInterop.QuitSubSystem(SdlInterop.InitGamepad | SdlInterop.InitJoystick);
                    SdlInterop.Quit();
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "SDL3 shutdown reported an error.");
                }

                sdlInitialized = false;
            }
        }

        inputDeviceCatalog.Clear("ProviderStatus_NoActiveProvider");
        return ValueTask.CompletedTask;
    }

    private ControllerSnapshot ReadSnapshotCore()
    {
        var devices = inputDeviceCatalog.Devices;
        if (devices.Count == 0)
        {
            CloseOpenedDevice();
            inputDeviceCatalog.SetProviderStatus("ProviderStatus_SdlNoDevices");
            return ControllerSnapshot.Empty("No SDL3 device detected") with { Timestamp = DateTimeOffset.UtcNow };
        }

        var selected = ResolveSelectedDevice(devices);
        if (selected is null)
        {
            CloseOpenedDevice();
            inputDeviceCatalog.SetProviderStatus("No controller selected. Automatic selection will use the first detected device.");
            return ControllerSnapshot.Empty("Select a controller") with { Timestamp = DateTimeOffset.UtcNow };
        }

        return !EnsureDeviceOpened(selected)
            ? (ControllerSnapshot.Empty(selected.DisplayName) with { Timestamp = DateTimeOffset.UtcNow })
            : openedDevice!.Kind == DeviceKind.Gamepad
            ? ReadGamepadSnapshot(openedDevice)
            : ReadJoystickSnapshot(openedDevice);
    }

    private InputDeviceInfo? ResolveSelectedDevice(IReadOnlyList<InputDeviceInfo> devices)
    {
        var selectedId = inputDeviceCatalog.SelectedDeviceId;
        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            var selected = devices.FirstOrDefault(device => string.Equals(device.Id, selectedId, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                return selected;
            }
        }

        return devices[0];
    }

    private bool EnsureDeviceOpened(InputDeviceInfo device)
    {
        if (openedDevice is not null && string.Equals(openedDevice.DeviceId, device.Id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        CloseOpenedDevice();

        if (!TryParseDeviceId(device.Id, out var kind, out var instanceId))
        {
            inputDeviceCatalog.SetProviderStatus($"Unable to parse selected SDL device id '{device.Id}'.");
            return false;
        }

        var handle = kind == DeviceKind.Gamepad
            ? SdlInterop.OpenGamepad(instanceId)
            : SdlInterop.OpenJoystick(instanceId);

        if (handle == IntPtr.Zero)
        {
            inputDeviceCatalog.SetProviderStatus($"Failed to open {device.DisplayName}: {SdlInterop.GetError()}");
            return false;
        }

        openedDevice = new OpenedDevice(device.Id, device.DisplayName, instanceId, kind, handle);
        return true;
    }

    private ControllerSnapshot ReadGamepadSnapshot(OpenedDevice device)
    {
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());

        buttons[ButtonId.South] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.South);
        buttons[ButtonId.East] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.East);
        buttons[ButtonId.West] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.West);
        buttons[ButtonId.North] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.North);
        buttons[ButtonId.LeftShoulder] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.LeftShoulder);
        buttons[ButtonId.RightShoulder] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.RightShoulder);
        buttons[ButtonId.Back] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.Back);
        buttons[ButtonId.Start] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.Start);
        buttons[ButtonId.Guide] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.Guide);
        buttons[ButtonId.LeftStick] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.LeftStick);
        buttons[ButtonId.RightStick] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.RightStick);
        buttons[ButtonId.DpadUp] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.DpadUp);
        buttons[ButtonId.DpadDown] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.DpadDown);
        buttons[ButtonId.DpadLeft] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.DpadLeft);
        buttons[ButtonId.DpadRight] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.DpadRight);
        buttons[ButtonId.Paddle1] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.LeftPaddle1);
        buttons[ButtonId.Paddle2] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.RightPaddle1);
        buttons[ButtonId.Paddle3] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.LeftPaddle2);
        buttons[ButtonId.Paddle4] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.RightPaddle2);
        buttons[ButtonId.Touchpad] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.Touchpad);
        buttons[ButtonId.Misc1] = SdlInterop.GetGamepadButton(device.Handle, SdlInterop.GamepadButton.Misc1);

        var leftTrigger = NormalizeGamepadTrigger(SdlInterop.GetGamepadAxis(device.Handle, SdlInterop.GamepadAxis.LeftTrigger));
        var rightTrigger = NormalizeGamepadTrigger(SdlInterop.GetGamepadAxis(device.Handle, SdlInterop.GamepadAxis.RightTrigger));

        buttons[ButtonId.LeftTriggerButton] = leftTrigger >= 0.65f;
        buttons[ButtonId.RightTriggerButton] = rightTrigger >= 0.65f;

        var touchContactCount = ReadTouchContactCount(device.Handle);
        if (touchContactCount > 0)
        {
            buttons[ButtonId.Touchpad] = true;
        }

        inputDeviceCatalog.SetProviderStatus($"Using SDL3 mapped gamepad: {device.DisplayName}.");

        return new ControllerSnapshot
        {
            DeviceName = device.DisplayName,
            VendorId   = SdlInterop.GetGamepadVendor(device.Handle),
            ProductId  = SdlInterop.GetGamepadProduct(device.Handle),
            LeftStick = new StickVector(
                NormalizeSignedAxis(SdlInterop.GetGamepadAxis(device.Handle, SdlInterop.GamepadAxis.LeftX)),
                -NormalizeSignedAxis(SdlInterop.GetGamepadAxis(device.Handle, SdlInterop.GamepadAxis.LeftY))).Clamp(),
            RightStick = new StickVector(
                NormalizeSignedAxis(SdlInterop.GetGamepadAxis(device.Handle, SdlInterop.GamepadAxis.RightX)),
                -NormalizeSignedAxis(SdlInterop.GetGamepadAxis(device.Handle, SdlInterop.GamepadAxis.RightY))).Clamp(),
            LeftTrigger = leftTrigger,
            RightTrigger = rightTrigger,
            TouchContactCount = touchContactCount,
            Buttons = buttons,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private ControllerSnapshot ReadJoystickSnapshot(OpenedDevice device)
    {
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        var buttonCount = Math.Max(0, SdlInterop.GetNumJoystickButtons(device.Handle));
        var axisCount = Math.Max(0, SdlInterop.GetNumJoystickAxes(device.Handle));
        var hatCount = Math.Max(0, SdlInterop.GetNumJoystickHats(device.Handle));

        if (buttonCount > 0)
        {
            buttons[ButtonId.South] = SdlInterop.GetJoystickButton(device.Handle, 0);
        }

        if (buttonCount > 1)
        {
            buttons[ButtonId.East] = SdlInterop.GetJoystickButton(device.Handle, 1);
        }

        if (buttonCount > 2)
        {
            buttons[ButtonId.West] = SdlInterop.GetJoystickButton(device.Handle, 2);
        }

        if (buttonCount > 3)
        {
            buttons[ButtonId.North] = SdlInterop.GetJoystickButton(device.Handle, 3);
        }

        if (buttonCount > 4)
        {
            buttons[ButtonId.LeftShoulder] = SdlInterop.GetJoystickButton(device.Handle, 4);
        }

        if (buttonCount > 5)
        {
            buttons[ButtonId.RightShoulder] = SdlInterop.GetJoystickButton(device.Handle, 5);
        }

        if (buttonCount > 6)
        {
            buttons[ButtonId.Back] = SdlInterop.GetJoystickButton(device.Handle, 6);
        }

        if (buttonCount > 7)
        {
            buttons[ButtonId.Start] = SdlInterop.GetJoystickButton(device.Handle, 7);
        }

        if (buttonCount > 8)
        {
            buttons[ButtonId.LeftStick] = SdlInterop.GetJoystickButton(device.Handle, 8);
        }

        if (buttonCount > 9)
        {
            buttons[ButtonId.RightStick] = SdlInterop.GetJoystickButton(device.Handle, 9);
        }

        if (buttonCount > 10)
        {
            buttons[ButtonId.Guide] = SdlInterop.GetJoystickButton(device.Handle, 10);
        }

        if (buttonCount > 11)
        {
            buttons[ButtonId.Misc1] = SdlInterop.GetJoystickButton(device.Handle, 11);
        }

        if (hatCount > 0)
        {
            var hat = SdlInterop.GetJoystickHat(device.Handle, 0);
            buttons[ButtonId.DpadUp] = (hat & SdlInterop.HatUp) == SdlInterop.HatUp;
            buttons[ButtonId.DpadRight] = (hat & SdlInterop.HatRight) == SdlInterop.HatRight;
            buttons[ButtonId.DpadDown] = (hat & SdlInterop.HatDown) == SdlInterop.HatDown;
            buttons[ButtonId.DpadLeft] = (hat & SdlInterop.HatLeft) == SdlInterop.HatLeft;
        }

        var leftTrigger = axisCount > 4
            ? NormalizePositiveHalfAxis(SdlInterop.GetJoystickAxis(device.Handle, 4))
            : 0f;
        var rightTrigger = axisCount > 5
            ? NormalizePositiveHalfAxis(SdlInterop.GetJoystickAxis(device.Handle, 5))
            : 0f;

        buttons[ButtonId.LeftTriggerButton] = leftTrigger >= 0.65f;
        buttons[ButtonId.RightTriggerButton] = rightTrigger >= 0.65f;

        inputDeviceCatalog.SetProviderStatus($"Using SDL3 generic joystick or HID fallback: {device.DisplayName}. Mapping is provisional until the binding editor is finished.");

        return new ControllerSnapshot
        {
            DeviceName = device.DisplayName,
            VendorId   = SdlInterop.GetJoystickVendor(device.Handle),
            ProductId  = SdlInterop.GetJoystickProduct(device.Handle),
            LeftStick = new StickVector(
                axisCount > 0 ? NormalizeSignedAxis(SdlInterop.GetJoystickAxis(device.Handle, 0)) : 0f,
                axisCount > 1 ? -NormalizeSignedAxis(SdlInterop.GetJoystickAxis(device.Handle, 1)) : 0f).Clamp(),
            RightStick = new StickVector(
                axisCount > 2 ? NormalizeSignedAxis(SdlInterop.GetJoystickAxis(device.Handle, 2)) : 0f,
                axisCount > 3 ? -NormalizeSignedAxis(SdlInterop.GetJoystickAxis(device.Handle, 3)) : 0f).Clamp(),
            LeftTrigger = leftTrigger,
            RightTrigger = rightTrigger,
            TouchContactCount = 0,
            Buttons = buttons,
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private static int ReadTouchContactCount(IntPtr gamepad)
    {
        var touchpads = Math.Max(0, SdlInterop.GetNumGamepadTouchpads(gamepad));
        var activeContacts = 0;

        for (var touchpadIndex = 0; touchpadIndex < touchpads; touchpadIndex++)
        {
            var fingers = Math.Max(0, SdlInterop.GetNumGamepadTouchpadFingers(gamepad, touchpadIndex));
            for (var fingerIndex = 0; fingerIndex < fingers; fingerIndex++)
            {
                if (!SdlInterop.GetGamepadTouchpadFinger(gamepad, touchpadIndex, fingerIndex, out var down, out _, out _, out _))
                {
                    continue;
                }

                if (down != 0)
                {
                    activeContacts++;
                }
            }
        }

        return activeContacts;
    }

    private void RefreshDeviceCatalog()
    {
        var devices = new List<InputDeviceInfo>();

        var gamepadsPointer = SdlInterop.GetGamepads(out var gamepadCount);
        try
        {
            for (var index = 0; index < gamepadCount; index++)
            {
                var instanceId = ReadJoystickId(gamepadsPointer, index);
                var name = SdlInterop.ReadString(SdlInterop.GetGamepadNameForIdPointer(instanceId));
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"SDL Gamepad {instanceId}";
                }

                devices.Add(new InputDeviceInfo(
                    $"sdl-gamepad-{instanceId}",
                    name,
                    true,
                    false,
                    SdlInterop.GetGamepadVendorForId(instanceId),
                    SdlInterop.GetGamepadProductForId(instanceId)));
            }
        }
        finally
        {
            if (gamepadsPointer != IntPtr.Zero)
            {
                SdlInterop.Free(gamepadsPointer);
            }
        }

        var joysticksPointer = SdlInterop.GetJoysticks(out var joystickCount);
        try
        {
            for (var index = 0; index < joystickCount; index++)
            {
                var instanceId = ReadJoystickId(joysticksPointer, index);
                if (SdlInterop.IsGamepad(instanceId))
                {
                    continue;
                }

                var name = SdlInterop.ReadString(SdlInterop.GetJoystickNameForIdPointer(instanceId));
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = $"SDL Joystick {instanceId}";
                }

                devices.Add(new InputDeviceInfo(
                    $"sdl-joystick-{instanceId}",
                    name,
                    true,
                    false,
                    SdlInterop.GetJoystickVendorForId(instanceId),
                    SdlInterop.GetJoystickProductForId(instanceId)));
            }
        }
        finally
        {
            if (joysticksPointer != IntPtr.Zero)
            {
                SdlInterop.Free(joysticksPointer);
            }
        }

        inputDeviceCatalog.ReplaceDevices(devices);

        if (devices.Count == 0)
        {
            inputDeviceCatalog.SetProviderStatus("ProviderStatus_SdlNoDevices");
            return;
        }

        // ProviderStatus_SdlActive's translation is "SDL3 unified input active — {0} gamepad(s) detected"
        inputDeviceCatalog.SetProviderStatus("ProviderStatus_SdlActive", devices.Count);
    }

    private void TryLoadOptionalMappings()
    {
        try
        {
            var mappingFile = Path.Combine(AppContext.BaseDirectory, "gamecontrollerdb.txt");
            if (!File.Exists(mappingFile))
            {
                return;
            }

            var added = SdlInterop.AddGamepadMappingsFromFile(mappingFile);
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("Loaded {MappingCount} SDL3 gamepad mapping entries from {MappingFile}.", added, mappingFile);
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Optional SDL3 gamepad mappings could not be loaded.");
        }
    }

    private void CloseOpenedDevice()
    {
        if (openedDevice is null)
        {
            return;
        }

        try
        {
            if (openedDevice.Kind == DeviceKind.Gamepad)
            {
                SdlInterop.CloseGamepad(openedDevice.Handle);
            }
            else
            {
                SdlInterop.CloseJoystick(openedDevice.Handle);
            }
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "SDL3 device close reported an error.");
        }
        finally
        {
            openedDevice = null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, nameof(SdlUnifiedInputSource));
    }

    private static float NormalizeSignedAxis(short value)
    {
        return value == short.MinValue ? -1f : Math.Clamp(value / 32767f, -1f, 1f);
    }

    private static float NormalizePositiveHalfAxis(short value)
    {
        return Math.Clamp(Math.Max(0f, NormalizeSignedAxis(value)), 0f, 1f);
    }

    private static float NormalizeGamepadTrigger(short value)
    {
        return Math.Clamp(value / 32767f, 0f, 1f);
    }

    private static uint ReadJoystickId(IntPtr pointer, int index)
    {
        return pointer == IntPtr.Zero
                ? 0u
                : unchecked((uint)Marshal.ReadInt32(pointer, index * sizeof(int)));
    }

    private static bool TryParseDeviceId(string? deviceId, out DeviceKind kind, out uint instanceId)
    {
        kind = DeviceKind.Gamepad;
        instanceId = 0;

        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return false;
        }

        var parts = deviceId.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3 || !uint.TryParse(parts[^1], out instanceId))
        {
            return false;
        }

        kind = string.Equals(parts[1], "joystick", StringComparison.OrdinalIgnoreCase)
            ? DeviceKind.Joystick
            : DeviceKind.Gamepad;

        return true;
    }

    private enum DeviceKind
    {
        Gamepad,
        Joystick
    }

    private sealed record OpenedDevice(string DeviceId, string DisplayName, uint InstanceId, DeviceKind Kind, IntPtr Handle);
}
