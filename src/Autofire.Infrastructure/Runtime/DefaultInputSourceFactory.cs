using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Autofire.Infrastructure.Runtime;

/// <summary>
/// Creates the input source for a profile. SDL3 unified input is the one
/// live provider (gamepads on every platform, plus keyboard/mouse
/// synthesis via Raw Input on Windows); <c>demo</c> and <c>none</c> exist
/// for UI iteration and idling. Legacy provider ids from the pre-SDL era
/// (XInput, OpenXInput, x360ce, PS3/DsHidMini, GameInput, Windows MIDI)
/// are transparently migrated to SDL so old profiles keep working.
/// </summary>
public sealed class DefaultInputSourceFactory(
    IServiceProvider serviceProvider,
    ILogger<DefaultInputSourceFactory> logger) : IInputSourceFactory
{
    private readonly IServiceProvider serviceProvider = serviceProvider;
    private readonly ILogger<DefaultInputSourceFactory> logger = logger;

    private static readonly string[] LegacyProviderIds =
    [
        "xinput", "openxinput", "x360ce", "ps3", "dualshock3",
        "gameinput", "windows-midi", "winmidi", "midi",
    ];

    public IInputSource Create(string? providerId)
    {
        var normalized = Normalize(providerId);

        if (LegacyProviderIds.Contains(normalized, StringComparer.Ordinal))
        {
            logger.LogInformation(
                "Input provider '{LegacyProvider}' has been retired; using SDL3 unified input instead.",
                normalized);
            return CreateSdlOrFallback();
        }

        return normalized switch
        {
            "none" => ActivatorUtilities.CreateInstance<NullInputSource>(
                serviceProvider, "No live input", "Live input is disabled for this profile."),
            "demo" => ActivatorUtilities.CreateInstance<DemoInputSource>(
                serviceProvider, "DemoInput (preview)"),
            "sdl"  => CreateSdlOrFallback(),
            _      => CreateUnknown(normalized),
        };
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

    private IInputSource CreateUnknown(string normalized)
    {
        logger.LogWarning(
            "Unknown input provider '{RequestedProvider}'; using SDL3 unified input instead.",
            normalized);
        return CreateSdlOrFallback();
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
            _ => normalized,
        };
    }
}
