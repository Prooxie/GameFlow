using System.Runtime.InteropServices;
using Autofire.Core.Enums;
using Autofire.Core.Models;
using Autofire.Infrastructure.Runtime.XInput;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime;

public sealed class XInputInputSource : IInputSource
{
    // Enumerate the four XInput slots at most once every 2 seconds.
    // XInputGetState on a disconnected slot is fast (~1 µs) but calling
    // ReplaceDevices + SetProviderStatus at 250 Hz floods the UI dispatcher.
    private const int CatalogRefreshEveryTicks = 500;   // 500 × 4 ms = 2 s

    private readonly Lock syncRoot = new();
    private readonly ILogger<XInputInputSource> logger;
    private readonly InputDeviceCatalog inputDeviceCatalog;
    private readonly IntPtr libraryHandle;
    private readonly XInputInterop.XInputGetStateDelegate? getState;
    private int tickCounter;
    private bool disposed;

    public XInputInputSource(ILogger<XInputInputSource> logger, InputDeviceCatalog inputDeviceCatalog)
    {
        this.logger = logger;
        this.inputDeviceCatalog = inputDeviceCatalog;

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("XInput is only available on Windows.");
        }

        if (!XInputInterop.TryLoad(out libraryHandle, out var loadedGetState) || loadedGetState is null)
        {
            throw new DllNotFoundException("Unable to load XInput. Windows XInput runtime was not found.");
        }

        getState = loadedGetState;

        // Enumerate once at startup.
        RefreshDeviceCatalog();
        logger.LogInformation("Initialized XInput input provider.");
    }

    public string DisplayName => "XInput (Windows)";

    public ValueTask<ControllerSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (syncRoot)
        {
            ThrowIfDisposed();

            // Re-enumerate at most every CatalogRefreshEveryTicks ticks,
            // not on every ReadAsync call.
            tickCounter++;
            if (tickCounter >= CatalogRefreshEveryTicks)
            {
                tickCounter = 0;
                RefreshDeviceCatalog();
            }

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
            if (libraryHandle != IntPtr.Zero)
            {
                NativeLibrary.Free(libraryHandle);
            }
        }

        inputDeviceCatalog.Clear("ProviderStatus_NoActiveProvider");
        return ValueTask.CompletedTask;
    }

    // ─── Private ──────────────────────────────────────────────────────────────

    private ControllerSnapshot ReadSnapshotCore()
    {
        var devices = inputDeviceCatalog.Devices;

        if (devices.Count == 0)
        {
            // Status was already set in RefreshDeviceCatalog — don't set it again here.
            return ControllerSnapshot.Empty("No XInput controller") with
            {
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        var selectedId = inputDeviceCatalog.SelectedDeviceId;
        var selected = devices.FirstOrDefault(d =>
            string.Equals(d.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? devices[0];

        var userIndex = ParseUserIndex(selected.Id);
        var result = GetState((uint)userIndex, out var state);

        if (result != XInputInterop.ErrorSuccess)
        {
            // Controller disconnected mid-session — schedule a catalog refresh next tick.
            tickCounter = CatalogRefreshEveryTicks;
            return ControllerSnapshot.Empty(selected.DisplayName) with
            {
                Timestamp = DateTimeOffset.UtcNow
            };
        }

        return new ControllerSnapshot
        {
            DeviceName = selected.DisplayName,
            LeftStick = new StickVector(
                ApplyDeadzone(XInputInterop.NormalizeAxis(state.Gamepad.ThumbLX), 0.12f),
                ApplyDeadzone(XInputInterop.NormalizeAxis(state.Gamepad.ThumbLY), 0.12f)).Clamp(),
            RightStick = new StickVector(
                ApplyDeadzone(XInputInterop.NormalizeAxis(state.Gamepad.ThumbRX), 0.12f),
                ApplyDeadzone(XInputInterop.NormalizeAxis(state.Gamepad.ThumbRY), 0.12f)).Clamp(),
            LeftTrigger = XInputInterop.NormalizeTrigger(state.Gamepad.LeftTrigger),
            RightTrigger = XInputInterop.NormalizeTrigger(state.Gamepad.RightTrigger),
            TouchContactCount = 0,
            Buttons = MapButtons(state.Gamepad),
            Timestamp = DateTimeOffset.UtcNow
        };
    }

    private void RefreshDeviceCatalog()
    {
        var devices = new List<InputDeviceInfo>();

        for (var i = 0; i < 4; i++)
        {
            var result = GetState((uint)i, out _);
            if (result == XInputInterop.ErrorSuccess)
            {
                devices.Add(new InputDeviceInfo(
                    $"xinput-{i}",
                    $"XInput Controller {i + 1}",
                    IsConnected: true,
                    IsSelected: false));
            }
        }

        inputDeviceCatalog.ReplaceDevices(devices);

        // Use existing ProviderStatus_XInput* localization keys so culture
        // changes flip the status text immediately (see InputDeviceCatalog
        // doc comment on its localization model).
        if (devices.Count == 0)
        {
            inputDeviceCatalog.SetProviderStatus("ProviderStatus_XInputNoDevices");
        }
        else
        {
            inputDeviceCatalog.SetProviderStatus("ProviderStatus_XInputActive", devices.Count);
        }
    }

    private uint GetState(uint userIndex, out XInputInterop.XInputState state)
    {
        var callback = getState ?? throw new ObjectDisposedException(nameof(XInputInputSource));
        return callback(userIndex, out state);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, nameof(XInputInputSource));
    }

    private static int ParseUserIndex(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return 0;
        }

        var parts = id.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 1 && int.TryParse(parts[^1], out var index)
            ? Math.Clamp(index, 0, 3)
            : 0;
    }

    private static float ApplyDeadzone(float value, float deadzone)
    {
        var magnitude = Math.Abs(value);
        if (magnitude <= deadzone)
        {
            return 0f;
        }

        var normalized = (magnitude - deadzone) / (1f - deadzone);
        return MathF.CopySign(Math.Clamp(normalized, 0f, 1f), value);
    }

    private static Dictionary<ButtonId, bool> MapButtons(XInputInterop.XInputGamepad gamepad)
    {
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        buttons[ButtonId.South] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.A);
        buttons[ButtonId.East] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.B);
        buttons[ButtonId.West] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.X);
        buttons[ButtonId.North] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.Y);
        buttons[ButtonId.LeftShoulder] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.LeftShoulder);
        buttons[ButtonId.RightShoulder] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.RightShoulder);
        buttons[ButtonId.LeftTriggerButton] = gamepad.LeftTrigger >= 240;
        buttons[ButtonId.RightTriggerButton] = gamepad.RightTrigger >= 240;
        buttons[ButtonId.Back] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.Back);
        buttons[ButtonId.Start] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.Start);
        buttons[ButtonId.Guide] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.Guide);
        buttons[ButtonId.LeftStick] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.LeftThumb);
        buttons[ButtonId.RightStick] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.RightThumb);
        buttons[ButtonId.DpadUp] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.DPadUp);
        buttons[ButtonId.DpadDown] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.DPadDown);
        buttons[ButtonId.DpadLeft] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.DPadLeft);
        buttons[ButtonId.DpadRight] = Has(gamepad.Buttons, XInputInterop.XInputGamepadButtons.DPadRight);
        return buttons;
    }

    private static bool Has(XInputInterop.XInputGamepadButtons value, XInputInterop.XInputGamepadButtons flag)
    {
        return (value & flag) == flag;
    }
}
