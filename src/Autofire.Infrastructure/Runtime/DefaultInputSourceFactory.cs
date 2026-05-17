using Autofire.Infrastructure.Configuration;
using Autofire.Infrastructure.Runtime.OpenXInput;
using Autofire.Infrastructure.Runtime.Ps3;
using Autofire.Infrastructure.Runtime.WindowsMidi;
using Autofire.Infrastructure.Runtime.X360ce;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autofire.Infrastructure.Runtime;

public sealed class DefaultInputSourceFactory(
    IServiceProvider serviceProvider,
    IOptions<AppRuntimeOptions> runtimeOptions,
    ILogger<DefaultInputSourceFactory> logger) : IInputSourceFactory
{
    private readonly IServiceProvider serviceProvider = serviceProvider;
    private readonly ILogger<DefaultInputSourceFactory> logger = logger;
    private readonly AppRuntimeOptions runtimeOptions = runtimeOptions.Value;

    public IInputSource Create(string? providerId)
    {
        var normalized = Normalize(providerId);

        return normalized switch
        {
            "none"        => ActivatorUtilities.CreateInstance<NullInputSource>(serviceProvider, "No live input", "Live input is disabled for this profile."),
            "demo"        => ActivatorUtilities.CreateInstance<DemoInputSource>(serviceProvider, "DemoInput (preview)"),
            "xinput"      => CreateXInputOrFallback(),
            "sdl" or "sdlgamepad" or "sdl3" or "sdl-unified" or "sdl-unified-gamepad" => CreateSdlOrFallback(),
            "gameinput"   => CreateGameInputOrFallback(),
            // ─── Step 6 scaffolds — see ScaffoldedInputSourceBase ─────
            "x360ce"      => ActivatorUtilities.CreateInstance<X360ceInputSource>(serviceProvider),
            "openxinput"  => ActivatorUtilities.CreateInstance<OpenXInputInputSource>(serviceProvider),
            "ps3" or "dualshock3" => ActivatorUtilities.CreateInstance<Ps3InputSource>(serviceProvider),
            "windows-midi" or "winmidi" or "midi" => ActivatorUtilities.CreateInstance<WindowsMidiInputSource>(serviceProvider),
            // ──────────────────────────────────────────────────────────
            _             => ActivatorUtilities.CreateInstance<NullInputSource>(serviceProvider, "No live input", $"Unknown input provider '{normalized}'.")
        };
    }

    private IInputSource CreateXInputOrFallback()
    {
        if (!OperatingSystem.IsWindows())
        {
            return CreateUnavailable("xinput", "XInput is only available on Windows.");
        }

        try
        {
            return ActivatorUtilities.CreateInstance<XInputInputSource>(serviceProvider);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "XInput could not be initialized.");
            return ActivatorUtilities.CreateInstance<NullInputSource>(
                serviceProvider,
                "XInput (unavailable)",
                $"XInput failed to initialize: {exception.Message}");
        }
    }

    private IInputSource CreateSdlOrFallback()
    {
        try
        {
            return ActivatorUtilities.CreateInstance<SdlUnifiedInputSource>(serviceProvider);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "SDL3 unified input could not be initialized.");
            return ActivatorUtilities.CreateInstance<NullInputSource>(
                serviceProvider,
                "SDL3 unified input (unavailable)",
                $"SDL3 unified input failed to initialize: {exception.Message}");
        }
    }

    private IInputSource CreateGameInputOrFallback()
    {
        if (!runtimeOptions.EnableExperimentalGameInput)
        {
            return CreateUnavailable(
                "gameinput",
                "Microsoft.GameInput is disabled in appsettings. Use SDL3 unified input for normal controller work.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return CreateUnavailable(
                "gameinput",
                "Microsoft.GameInput is only available on Windows.");
        }

        try
        {
            return ActivatorUtilities.CreateInstance<GameInputInputSource>(serviceProvider);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Microsoft.GameInput could not be initialized.");
            return ActivatorUtilities.CreateInstance<NullInputSource>(
                serviceProvider,
                "Microsoft.GameInput (unavailable)",
                $"GameInput failed to initialize: {exception.Message}");
        }
    }

    private Autofire.Infrastructure.Runtime.NullInputSource CreateUnavailable(string requestedProvider, string reason)
    {
        logger.LogWarning(
            "Requested input provider {RequestedProvider} is unavailable. {Reason}",
            requestedProvider,
            reason);

        return ActivatorUtilities.CreateInstance<NullInputSource>(
            serviceProvider,
            $"{requestedProvider} unavailable",
            reason);
    }

    private static string Normalize(string? providerId)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return "sdl";
        }

        var normalized = providerId.Trim().ToLowerInvariant();
        return normalized switch
        {
            "sdlgamepad" or "sdl3" or "sdl-unified" or "sdl-unified-gamepad" => "sdl",
            _ => normalized
        };
    }
}
