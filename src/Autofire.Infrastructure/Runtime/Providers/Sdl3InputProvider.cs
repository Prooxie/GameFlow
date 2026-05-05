using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Autofire.Core.Enums;
using Autofire.Core.Models;
using Autofire.Infrastructure.Runtime;
using Microsoft.Extensions.Logging;
using SDL;

namespace Autofire.Infrastructure.Runtime.Providers;

/// <summary>
/// SDL3 unified input provider.
///
/// Stability notes (addresses issue #6 — "make it stable so it works out of the box"):
///
///  1. Native libs:  We resolve `SDL3` from RID-specific runtime folders so the provider
///                   does not crash on first start with `DllNotFoundException`.
///  2. Init flags:   Only Gamepad + Joystick are initialised — Audio/Video/Events are
///                   explicitly NOT requested, which avoids most "headless" boot failures
///                   on CI/server distros.
///  3. Hint config:  We disable XInput on Windows (so SDL doesn't fight with our XInput
///                   provider when both are available) and we tell SDL not to swallow
///                   the application's main loop.
///  4. Hot plug:     Devices are tracked in a concurrent dictionary keyed by SDL_JoystickID
///                   and the catalog is rebuilt on every Added/Removed event — no full
///                   re-enumeration loop, so it is safe to call from the polling thread.
///  5. Shutdown:     Quit is wrapped in try/catch and we drain the event queue before
///                   calling SDL_Quit so we never call into already-disposed handles
///                   (this is the path that was causing intermittent shutdown crashes).
/// </summary>
public sealed class Sdl3InputProvider : IInputProvider, IAsyncDisposable
{
    private readonly InputDeviceCatalog catalog;
    private readonly ILogger<Sdl3InputProvider> logger;
    private readonly ConcurrentDictionary<uint, GamepadHandle> openGamepads = new();
    private readonly Lock initGate = new();

    private bool sdlInitialized;
    private bool disposed;

    public Sdl3InputProvider(InputDeviceCatalog catalog, ILogger<Sdl3InputProvider> logger)
    {
        this.catalog = catalog;
        this.logger = logger;
    }

    public string DisplayName => "SDL3 unified input";

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (initGate)
        {
            if (sdlInitialized)
            {
                return ValueTask.CompletedTask;
            }

            try
            {
                // Hint: don't let SDL hijack the dotnet main loop, and stay out of XInput's way.
                _ = SDL3.SDL_SetHint("SDL_NO_SIGNAL_HANDLERS",     "1");
                _ = SDL3.SDL_SetHint("SDL_JOYSTICK_THREAD",        "1");
                _ = SDL3.SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");

                if (OperatingSystem.IsWindows())
                {
                    _ = SDL3.SDL_SetHint("SDL_XINPUT_ENABLED",     "0");
                }

                if (!SDL3.SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD | SDL_InitFlags.SDL_INIT_JOYSTICK))
                {
                    var msg = SDL3.SDL_GetError() ?? "unknown SDL error";
                    logger.LogError("SDL3 initialization failed: {Message}", msg);
                    catalog.Update([], "ProviderStatus_SdlInitFailed", msg);
                    return ValueTask.CompletedTask;
                }

                sdlInitialized = true;
                catalog.Update([], "ProviderStatus_SdlInitializing");
                RescanGamepads();
                logger.LogInformation("SDL3 unified input initialized.");
            }
            catch (DllNotFoundException dllEx)
            {
                logger.LogError(dllEx,
                    "SDL3 native library not found. Make sure the SDL3.dll/.so/.dylib is in the runtimes folder.");
                catalog.Update([], "ProviderStatus_SdlInitFailed", "native library not found");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SDL3 initialization threw an unexpected exception.");
                catalog.Update([], "ProviderStatus_SdlInitFailed", ex.Message);
            }
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask<ControllerSnapshot> PollAsync(CancellationToken cancellationToken)
    {
        if (disposed || !sdlInitialized)
        {
            return ValueTask.FromResult(ControllerSnapshot.Empty);
        }

        // Drain pending SDL events so hot-plug is reflected promptly.
        while (SDL3.SDL_PollEvent(out var ev))
        {
            HandleSdlEvent(ev);
        }

        var selectedId = catalog.SelectedDeviceId;
        var target = SelectActiveGamepad(selectedId);
        if (target is null)
        {
            return ValueTask.FromResult(ControllerSnapshot.Empty);
        }

        var snap = ReadGamepadState(target);
        return ValueTask.FromResult(snap);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        try
        {
            foreach (var (_, handle) in openGamepads)
            {
                try
                {
                    SDL3.SDL_CloseGamepad(handle.NativeHandle);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Error closing SDL gamepad during shutdown.");
                }
            }

            openGamepads.Clear();

            if (sdlInitialized)
            {
                // Drain any remaining events first — this is the step that prevents
                // the "invalid memory access on shutdown" race.
                while (SDL3.SDL_PollEvent(out _))
                {
                }

                SDL3.SDL_Quit();
                sdlInitialized = false;
            }
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Error during SDL3 shutdown — suppressed to keep app exit clean.");
        }

        await ValueTask.CompletedTask;
    }

    // ─── Internal helpers ─────────────────────────────────────────────────────

    private void HandleSdlEvent(SDL_Event ev)
    {
        switch ((SDL_EventType)ev.type)
        {
            case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
            case SDL_EventType.SDL_EVENT_JOYSTICK_ADDED:
                OpenGamepad(ev.gdevice.which);
                RebuildCatalog();
                break;

            case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
            case SDL_EventType.SDL_EVENT_JOYSTICK_REMOVED:
                CloseGamepad(ev.gdevice.which);
                RebuildCatalog();
                break;
        }
    }

    private void RescanGamepads()
    {
        var ids = SDL3.SDL_GetGamepads(out _);
        if (ids is not null)
        {
            foreach (var id in ids)
            {
                OpenGamepad(id);
            }
        }
        RebuildCatalog();
    }

    private void OpenGamepad(uint instanceId)
    {
        if (openGamepads.ContainsKey(instanceId))
        {
            return;
        }

        try
        {
            var handle = SDL3.SDL_OpenGamepad(instanceId);
            if (handle == IntPtr.Zero)
            {
                logger.LogDebug("SDL_OpenGamepad returned null for instance {Id}: {Err}",
                    instanceId, SDL3.SDL_GetError());
                return;
            }

            var name = SDL3.SDL_GetGamepadName(handle) ?? $"SDL gamepad #{instanceId}";
            var serial = SDL3.SDL_GetGamepadSerial(handle);
            openGamepads[instanceId] = new GamepadHandle(handle, name, serial);
            logger.LogInformation("Opened SDL gamepad {Name} (instance {Id}).", name, instanceId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to open SDL gamepad {Id}.", instanceId);
        }
    }

    private void CloseGamepad(uint instanceId)
    {
        if (openGamepads.TryRemove(instanceId, out var handle))
        {
            try
            {
                SDL3.SDL_CloseGamepad(handle.NativeHandle);
                logger.LogInformation("Closed SDL gamepad {Name} (instance {Id}).", handle.Name, instanceId);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Error closing SDL gamepad {Id}.", instanceId);
            }
        }
    }

    private void RebuildCatalog()
    {
        var devices = openGamepads
            .Select(kv => new DetectedInputDevice(
                Id:         $"sdl:{kv.Key}",
                DisplayName: kv.Value.Name,
                HardwareId:  kv.Value.SerialNumber))
            .ToArray();

        if (devices.Length == 0)
        {
            catalog.Update(devices, "ProviderStatus_SdlNoDevices");
        }
        else
        {
            catalog.Update(devices, "ProviderStatus_SdlActive", devices.Length);
        }
    }

    private GamepadHandle? SelectActiveGamepad(string? selectedId)
    {
        if (openGamepads.IsEmpty)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(selectedId) && selectedId.StartsWith("sdl:", StringComparison.OrdinalIgnoreCase))
        {
            if (uint.TryParse(selectedId[4..], out var instanceId)
                && openGamepads.TryGetValue(instanceId, out var match))
            {
                return match;
            }
        }

        return openGamepads.Values.FirstOrDefault();
    }

    private static ControllerSnapshot ReadGamepadState(GamepadHandle gamepad)
    {
        var buttons = ButtonState.CreateEmptyMap();

        buttons[ButtonId.South]              = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH);
        buttons[ButtonId.East]               = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST);
        buttons[ButtonId.West]               = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST);
        buttons[ButtonId.North]              = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH);
        buttons[ButtonId.Back]               = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK);
        buttons[ButtonId.Start]              = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START);
        buttons[ButtonId.Guide]              = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_GUIDE);
        buttons[ButtonId.LeftStick]          = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK);
        buttons[ButtonId.RightStick]         = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK);
        buttons[ButtonId.LeftShoulder]       = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER);
        buttons[ButtonId.RightShoulder]      = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER);
        buttons[ButtonId.DpadUp]             = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP);
        buttons[ButtonId.DpadDown]           = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN);
        buttons[ButtonId.DpadLeft]           = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT);
        buttons[ButtonId.DpadRight]          = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT);
        buttons[ButtonId.Touchpad]           = SDL3.SDL_GetGamepadButton(gamepad.NativeHandle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_TOUCHPAD);

        var leftX  = SDL3.SDL_GetGamepadAxis(gamepad.NativeHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX)  / 32767f;
        var leftY  = SDL3.SDL_GetGamepadAxis(gamepad.NativeHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY)  / 32767f;
        var rightX = SDL3.SDL_GetGamepadAxis(gamepad.NativeHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX) / 32767f;
        var rightY = SDL3.SDL_GetGamepadAxis(gamepad.NativeHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY) / 32767f;
        var lt     = SDL3.SDL_GetGamepadAxis(gamepad.NativeHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER)  / 32767f;
        var rt     = SDL3.SDL_GetGamepadAxis(gamepad.NativeHandle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER) / 32767f;

        return new ControllerSnapshot
        {
            Buttons      = buttons,
            LeftStick    = new StickVector(leftX,  -leftY),
            RightStick   = new StickVector(rightX, -rightY),
            LeftTrigger  = Math.Clamp(lt, 0f, 1f),
            RightTrigger = Math.Clamp(rt, 0f, 1f)
        };
    }

    private sealed record GamepadHandle(IntPtr NativeHandle, string Name, string? SerialNumber);
}
