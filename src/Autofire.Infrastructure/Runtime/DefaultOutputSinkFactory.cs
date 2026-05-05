using Autofire.Infrastructure.Configuration;
using Autofire.Infrastructure.Runtime.ViGEm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autofire.Infrastructure.Runtime;

public sealed class DefaultOutputSinkFactory(
    IServiceProvider serviceProvider,
    IOptions<AppRuntimeOptions> runtimeOptions,
    ILogger<DefaultOutputSinkFactory> logger) : IOutputSinkFactory
{
    private readonly IServiceProvider serviceProvider = serviceProvider;
    private readonly ILogger<DefaultOutputSinkFactory> logger = logger;
    private readonly AppRuntimeOptions runtimeOptions = runtimeOptions.Value;

    public IOutputSink Create(string? providerId)
    {
        var normalized = Normalize(providerId);

        return normalized switch
        {
            "preview"                           => ActivatorUtilities.CreateInstance<PreviewOutputSink>(serviceProvider),
            "vigem-xbox360" or "vigem-xbox"     => CreateViGEmXbox360OrFallback(),
            "vigem-ds4" or "vigem-dualshock4"   => CreateViGEmDs4OrFallback(),
            "vigem-ds5" or "vigem-dualsense"    => CreateViGEmDs5OrFallback(),
            _                                   => CreateUnknownFallback(normalized),
        };
    }

    private IOutputSink CreateViGEmXbox360OrFallback()
    {
        if (!runtimeOptions.EnableViGEm)
        {
            return CreateFallback("vigem-xbox360",
                "ViGEm output is disabled in appsettings. Set Runtime.EnableViGEm to true.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return CreateFallback("vigem-xbox360", "ViGEm is only available on Windows.");
        }

        try
        {
            return ActivatorUtilities.CreateInstance<ViGEmXbox360OutputSink>(serviceProvider);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "ViGEm Xbox 360 virtual controller could not be created.");
            return CreateFallback("vigem-xbox360",
                $"ViGEm failed to initialize: {exception.Message} — " +
                "Make sure the ViGEm Bus driver is installed: https://github.com/nefarius/ViGEmBus/releases");
        }
    }

    private IOutputSink CreateViGEmDs4OrFallback()
    {
        if (!runtimeOptions.EnableViGEm)
        {
            return CreateFallback("vigem-ds4",
                "ViGEm output is disabled in appsettings. Set Runtime.EnableViGEm to true.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return CreateFallback("vigem-ds4", "ViGEm is only available on Windows.");
        }

        try
        {
            return ActivatorUtilities.CreateInstance<ViGEmDualShock4OutputSink>(serviceProvider);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "ViGEm DualShock 4 virtual controller could not be created.");
            return CreateFallback("vigem-ds4",
                $"ViGEm failed to initialize: {exception.Message} — " +
                "Make sure the ViGEm Bus driver is installed: https://github.com/nefarius/ViGEmBus/releases");
        }
    }

    private IOutputSink CreateViGEmDs5OrFallback()
    {
        if (!runtimeOptions.EnableViGEm)
        {
            return CreateFallback("vigem-ds5",
                "ViGEm output is disabled in appsettings. Set Runtime.EnableViGEm to true.");
        }

        if (!OperatingSystem.IsWindows())
        {
            return CreateFallback("vigem-ds5", "ViGEm is only available on Windows.");
        }

        try
        {
            return ActivatorUtilities.CreateInstance<ViGEmDualSenseOutputSink>(serviceProvider);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "ViGEm DualSense virtual controller could not be created.");
            return CreateFallback("vigem-ds5",
                $"ViGEm failed to initialize: {exception.Message} — " +
                "Make sure the ViGEm Bus driver is installed: https://github.com/nefarius/ViGEmBus/releases");
        }
    }

    private PreviewOutputSink CreateUnknownFallback(string normalized)
    {
        logger.LogWarning(
            "Requested output provider {RequestedProvider} is not recognised. Falling back to preview output.",
            normalized);

        return ActivatorUtilities.CreateInstance<PreviewOutputSink>(serviceProvider);
    }

    private PreviewOutputSink CreateFallback(string requestedProvider, string reason)
    {
        logger.LogWarning(
            "Requested output provider {RequestedProvider} is unavailable. {Reason} Falling back to preview output.",
            requestedProvider, reason);

        return ActivatorUtilities.CreateInstance<PreviewOutputSink>(serviceProvider);
    }

    private static string Normalize(string? providerId)
    {
        return string.IsNullOrWhiteSpace(providerId)
            ? "preview"
            : providerId.Trim().ToLowerInvariant();
    }
}
