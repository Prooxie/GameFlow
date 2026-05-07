using System.Runtime.InteropServices;
using Autofire.Core.Enums;
using Autofire.Core.Models;
using Autofire.Infrastructure.Runtime.GameInput;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime;

public sealed class GameInputInputSource : IInputSource
{
    private readonly Lock syncRoot = new();
    private readonly ILogger<GameInputInputSource> logger;
    private readonly InputDeviceCatalog inputDeviceCatalog;
    private readonly IntPtr libraryHandle;
    private readonly IntPtr gameInputHandle;
    private readonly GameInputInterop.GetCurrentReadingDelegate getCurrentReading;
    private readonly GameInputInterop.RegisterDeviceCallbackDelegate registerDeviceCallback;
    private readonly GameInputInterop.UnregisterCallbackDelegate unregisterCallback;
    private readonly GameInputInterop.DeviceCallbackDelegate deviceCallback;
    private readonly Dictionary<string, DeviceEntry> devices = new(StringComparer.OrdinalIgnoreCase);

    private ulong callbackToken;
    private bool callbackRegistered;
    private bool disposed;

    public GameInputInputSource(ILogger<GameInputInputSource> logger, InputDeviceCatalog inputDeviceCatalog)
    {
        this.logger = logger;
        this.inputDeviceCatalog = inputDeviceCatalog;

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Microsoft.GameInput is only available on Windows.");
        }

        if (!GameInputInterop.TryLoadGameInput(out libraryHandle, out var create) || create is null)
        {
            throw new DllNotFoundException(
                "Unable to load gameinput.dll. Install the Microsoft.GameInput redistributable or switch the input provider.");
        }

        var hr = create(out gameInputHandle);
        if (!GameInputInterop.Succeeded(hr) || gameInputHandle == IntPtr.Zero)
        {
            if (libraryHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(libraryHandle);
            }

            throw new InvalidOperationException($"GameInputCreate failed with HRESULT 0x{hr:X8}.");
        }

        getCurrentReading = GameInputInterop.GetVirtualMethod<GameInputInterop.GetCurrentReadingDelegate>(
            gameInputHandle,
            GameInputInterop.IGameInputGetCurrentReadingSlot);

        registerDeviceCallback = GameInputInterop.GetVirtualMethod<GameInputInterop.RegisterDeviceCallbackDelegate>(
            gameInputHandle,
            GameInputInterop.IGameInputRegisterDeviceCallbackSlot);

        unregisterCallback = GameInputInterop.GetVirtualMethod<GameInputInterop.UnregisterCallbackDelegate>(
            gameInputHandle,
            GameInputInterop.IGameInputUnregisterCallbackSlot);

        deviceCallback = OnDeviceChanged;

        try
        {
            RegisterCallbacks();
            PublishCatalog();
        }
        catch
        {
            GameInputInterop.Release(gameInputHandle);
            if (libraryHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(libraryHandle);
            }

            throw;
        }

        logger.LogInformation("Initialized Microsoft.GameInput input provider.");
    }

    public string DisplayName => "Microsoft.GameInput (Windows)";

    public ValueTask<ControllerSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            ThrowIfDisposed();
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

            if (callbackRegistered)
            {
                try
                {
                    _ = unregisterCallback(gameInputHandle, callbackToken);
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "Failed to unregister Microsoft.GameInput callback cleanly.");
                }

                callbackRegistered = false;
                callbackToken = 0;
            }

            foreach (var entry in devices.Values)
            {
                GameInputInterop.Release(entry.DeviceHandle);
            }

            devices.Clear();
            GameInputInterop.Release(gameInputHandle);

            if (libraryHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(libraryHandle);
            }
        }

        inputDeviceCatalog.Clear("ProviderStatus_NoActiveProvider");
        logger.LogInformation("Disposed Microsoft.GameInput input provider.");
        return ValueTask.CompletedTask;
    }

    private void RegisterCallbacks()
    {
        var hr = registerDeviceCallback(
            gameInputHandle,
            IntPtr.Zero,
            GameInputInterop.GameInputKind.Gamepad,
            GameInputInterop.GameInputDeviceStatus.Connected,
            GameInputInterop.GameInputEnumerationKind.BlockingEnumeration,
            IntPtr.Zero,
            deviceCallback,
            out callbackToken);

        if (!GameInputInterop.Succeeded(hr))
        {
            throw new InvalidOperationException($"RegisterDeviceCallback failed with HRESULT 0x{hr:X8}.");
        }

        callbackRegistered = true;
    }

    private ControllerSnapshot ReadSnapshotCore()
    {
        var now = DateTimeOffset.UtcNow;
        var selected = ResolveSelectedDevice();
        if (selected.State == SelectedDeviceState.NoDevices)
        {
            inputDeviceCatalog.SetProviderStatus("No compatible controller was detected by Microsoft.GameInput.");
            return ControllerSnapshot.Empty("No compatible controller detected") with { Timestamp = now };
        }

        if (selected.State == SelectedDeviceState.SelectionRequired)
        {
            inputDeviceCatalog.SetProviderStatus("Multiple controllers are connected. Select the controller you want to read in the dashboard.");
            return ControllerSnapshot.Empty("Select a controller") with { Timestamp = now };
        }

        var hr = getCurrentReading(
            gameInputHandle,
            GameInputInterop.GameInputKind.Gamepad,
            selected.Device?.DeviceHandle ?? IntPtr.Zero,
            out var readingHandle);

        if (!GameInputInterop.Succeeded(hr) || readingHandle == IntPtr.Zero)
        {
            inputDeviceCatalog.SetProviderStatus(selected.Device is null
                ? "Waiting for GameInput readings."
                : $"Connected to {selected.Device.DisplayName}, waiting for input readings.");

            return ControllerSnapshot.Empty(selected.Device?.DisplayName ?? DisplayName) with { Timestamp = now };
        }

        try
        {
            var getGamepadState = GameInputInterop.GetVirtualMethod<GameInputInterop.GetGamepadStateDelegate>(
                readingHandle,
                GameInputInterop.IGameInputReadingGetGamepadStateSlot);

            if (!getGamepadState(readingHandle, out var state))
            {
                return ControllerSnapshot.Empty(selected.Device?.DisplayName ?? DisplayName) with { Timestamp = now };
            }

            inputDeviceCatalog.SetProviderStatus(selected.Device is null
                ? "Using Microsoft.GameInput aggregate gamepad reading."
                : $"Using Microsoft.GameInput device: {selected.Device.DisplayName}.");

            return new ControllerSnapshot
            {
                DeviceName = selected.Device?.DisplayName ?? DisplayName,
                LeftStick = new StickVector(state.LeftThumbstickX, state.LeftThumbstickY).Clamp(),
                RightStick = new StickVector(state.RightThumbstickX, state.RightThumbstickY).Clamp(),
                LeftTrigger = Math.Clamp(state.LeftTrigger, 0f, 1f),
                RightTrigger = Math.Clamp(state.RightTrigger, 0f, 1f),
                TouchContactCount = 0,
                Buttons = MapButtons(state.Buttons),
                Timestamp = now
            };
        }
        finally
        {
            GameInputInterop.Release(readingHandle);
        }
    }

    private SelectedDevice ResolveSelectedDevice()
    {
        if (devices.Count == 0)
        {
            return new SelectedDevice(SelectedDeviceState.NoDevices, null);
        }

        var selectedId = inputDeviceCatalog.SelectedDeviceId;
        return string.IsNullOrWhiteSpace(selectedId)
            ? devices.Count == 1
                ? new SelectedDevice(SelectedDeviceState.Ready, devices.Values.First())
                : new SelectedDevice(SelectedDeviceState.SelectionRequired, null)
            : devices.TryGetValue(selectedId, out var selected)
            ? new SelectedDevice(SelectedDeviceState.Ready, selected)
            : devices.Count == 1
            ? new SelectedDevice(SelectedDeviceState.Ready, devices.Values.First())
            : new SelectedDevice(SelectedDeviceState.SelectionRequired, null);
    }

    private void OnDeviceChanged(
        ulong token,
        IntPtr context,
        IntPtr device,
        ulong timestamp,
        GameInputInterop.GameInputDeviceStatus currentStatus,
        GameInputInterop.GameInputDeviceStatus previousStatus)
    {
        if (device == IntPtr.Zero)
        {
            return;
        }

        try
        {
            DeviceMetadata metadata;
            lock (syncRoot)
            {
                if (disposed)
                {
                    return;
                }

                metadata = ReadDeviceMetadata(device);
                var isConnected = (currentStatus & GameInputInterop.GameInputDeviceStatus.Connected) == GameInputInterop.GameInputDeviceStatus.Connected;

                if (isConnected)
                {
                    GameInputInterop.AddRef(device);
                    if (devices.TryGetValue(metadata.Id, out var existing))
                    {
                        GameInputInterop.Release(existing.DeviceHandle);
                    }

                    devices[metadata.Id] = new DeviceEntry(device, metadata.Id, metadata.DisplayName, metadata.VendorId, metadata.ProductId);
                }
                else if (!string.IsNullOrWhiteSpace(metadata.Id) && devices.Remove(metadata.Id, out var removed))
                {
                    GameInputInterop.Release(removed.DeviceHandle);
                }
                else
                {
                    var match = devices.FirstOrDefault(pair => pair.Value.DeviceHandle == device);
                    if (!string.IsNullOrWhiteSpace(match.Key))
                    {
                        _ = devices.Remove(match.Key, out var removedByHandle);
                        if (removedByHandle is not null)
                        {
                            GameInputInterop.Release(removedByHandle.DeviceHandle);
                        }
                    }
                }
            }

            PublishCatalog();
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Failed to process Microsoft.GameInput device callback.");
        }
    }

    private DeviceMetadata ReadDeviceMetadata(IntPtr device)
    {
        var getDeviceInfo = GameInputInterop.GetVirtualMethod<GameInputInterop.GetDeviceInfoDelegate>(
            device,
            GameInputInterop.IGameInputDeviceGetDeviceInfoSlot);

        var hr = getDeviceInfo(device, out var deviceInfoPointer);
        if (!GameInputInterop.Succeeded(hr) || deviceInfoPointer == IntPtr.Zero)
        {
            return new DeviceMetadata(
                $"ptr-{device.ToInt64():x}",
                $"Controller {devices.Count + 1}",
                0,
                0);
        }

        var info = Marshal.PtrToStructure<GameInputInterop.GameInputDeviceInfo>(deviceInfoPointer);
        var id = GameInputInterop.DeviceIdToString(info.DeviceId);
        if (string.IsNullOrWhiteSpace(id))
        {
            id = $"ptr-{device.ToInt64():x}";
        }

        var displayName = GameInputInterop.PtrToUtf8String(info.DisplayName);
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = $"Controller {devices.Count + 1}";
        }

        return new DeviceMetadata(id, displayName, info.VendorId, info.ProductId);
    }

    private void PublishCatalog()
    {
        List<InputDeviceInfo> catalogSnapshot;
        string status;

        lock (syncRoot)
        {
            catalogSnapshot = [.. devices.Values
                .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new InputDeviceInfo(entry.Id, entry.DisplayName, true, false, entry.VendorId, entry.ProductId))];

            status = catalogSnapshot.Count switch
            {
                0 => "No compatible controller detected.",
                1 => $"1 controller detected: {catalogSnapshot[0].DisplayName}.",
                _ => $"{catalogSnapshot.Count} controllers detected. Choose the one you want to read."
            };
        }

        inputDeviceCatalog.ReplaceDevices(catalogSnapshot);
        inputDeviceCatalog.SetProviderStatus(status);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, nameof(GameInputInputSource));
    }

    private static System.Collections.Generic.Dictionary<ButtonId, bool> MapButtons(GameInputInterop.GameInputGamepadButtons buttons)
    {
        var mapped = ButtonState.Clone(ButtonState.CreateEmptyMap());

        Set(mapped, ButtonId.South, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.A));
        Set(mapped, ButtonId.East, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.B));
        Set(mapped, ButtonId.West, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.X));
        Set(mapped, ButtonId.North, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.Y));
        Set(mapped, ButtonId.LeftShoulder, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.LeftShoulder));
        Set(mapped, ButtonId.RightShoulder, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.RightShoulder));
        Set(mapped, ButtonId.LeftTriggerButton, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.LeftTriggerButton));
        Set(mapped, ButtonId.RightTriggerButton, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.RightTriggerButton));
        Set(mapped, ButtonId.Back, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.View));
        Set(mapped, ButtonId.Start, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.Menu));
        Set(mapped, ButtonId.LeftStick, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.LeftThumbstick));
        Set(mapped, ButtonId.RightStick, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.RightThumbstick));
        Set(mapped, ButtonId.DpadUp, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.DPadUp));
        Set(mapped, ButtonId.DpadDown, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.DPadDown));
        Set(mapped, ButtonId.DpadLeft, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.DPadLeft));
        Set(mapped, ButtonId.DpadRight, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.DPadRight));
        Set(mapped, ButtonId.Paddle1, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.PaddleLeft1));
        Set(mapped, ButtonId.Paddle2, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.PaddleLeft2));
        Set(mapped, ButtonId.Paddle3, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.PaddleRight1));
        Set(mapped, ButtonId.Paddle4, HasButton(buttons, GameInputInterop.GameInputGamepadButtons.PaddleRight2));
        Set(mapped, ButtonId.Misc1, HasAnyButton(buttons, GameInputInterop.GameInputGamepadButtons.C | GameInputInterop.GameInputGamepadButtons.Z));

        return mapped;
    }

    private static void Set(Dictionary<ButtonId, bool> map, ButtonId button, bool isPressed)
    {
        map[button] = isPressed;
    }

    private static bool HasButton(GameInputInterop.GameInputGamepadButtons value, GameInputInterop.GameInputGamepadButtons flag)
    {
        return (value & flag) == flag;
    }

    private static bool HasAnyButton(GameInputInterop.GameInputGamepadButtons value, GameInputInterop.GameInputGamepadButtons flags)
    {
        return (value & flags) != 0;
    }

    private sealed record DeviceEntry(IntPtr DeviceHandle, string Id, string DisplayName, ushort VendorId, ushort ProductId);

    private sealed record DeviceMetadata(string Id, string DisplayName, ushort VendorId, ushort ProductId);

    private readonly record struct SelectedDevice(SelectedDeviceState State, DeviceEntry? Device);

    private enum SelectedDeviceState
    {
        NoDevices,
        SelectionRequired,
        Ready
    }
}
