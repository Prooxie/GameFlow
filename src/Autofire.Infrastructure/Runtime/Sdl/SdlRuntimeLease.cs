using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime.Sdl;

internal static class SdlRuntimeLease
{
    private static readonly object SyncRoot = new();
    private static int referenceCount;
    private static bool initialized;

    public static IDisposable Acquire(ILogger logger)
    {
        lock (SyncRoot)
        {
            if (!initialized)
            {
                SdlGamepadInterop.SetHint(SdlGamepadInterop.SDL_HINT_JOYSTICK_HIDAPI, "1");
                SdlGamepadInterop.SetHint(SdlGamepadInterop.SDL_HINT_JOYSTICK_DIRECTINPUT, "1");
                SdlGamepadInterop.SetHint(SdlGamepadInterop.SDL_HINT_XINPUT_ENABLED, "1");
                SdlGamepadInterop.SetHint(SdlGamepadInterop.SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");
                SdlGamepadInterop.SetHint(SdlGamepadInterop.SDL_HINT_AUTO_UPDATE_JOYSTICKS, "0");

                var initFlags = SdlGamepadInterop.SDL_INIT_GAMEPAD | SdlGamepadInterop.SDL_INIT_JOYSTICK | SdlGamepadInterop.SDL_INIT_HAPTIC;
                if (!SdlGamepadInterop.Init(initFlags))
                {
                    throw new InvalidOperationException($"SDL_Init failed: {SdlGamepadInterop.GetLastError()}");
                }

                initialized = true;
                logger.LogInformation("Initialized SDL3 gamepad runtime.");
            }

            referenceCount++;
            return new Lease(logger);
        }
    }

    private static void Release(ILogger logger)
    {
        lock (SyncRoot)
        {
            if (referenceCount <= 0)
            {
                return;
            }

            referenceCount--;
            if (referenceCount > 0 || !initialized)
            {
                return;
            }

            SdlGamepadInterop.QuitSubSystem(SdlGamepadInterop.SDL_INIT_GAMEPAD | SdlGamepadInterop.SDL_INIT_JOYSTICK | SdlGamepadInterop.SDL_INIT_HAPTIC);
            SdlGamepadInterop.Quit();
            initialized = false;
            logger.LogInformation("Shut down SDL3 gamepad runtime.");
        }
    }

    private sealed class Lease : IDisposable
    {
        private readonly ILogger logger;
        private bool disposed;

        public Lease(ILogger logger)
        {
            this.logger = logger;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Release(logger);
        }
    }
}
