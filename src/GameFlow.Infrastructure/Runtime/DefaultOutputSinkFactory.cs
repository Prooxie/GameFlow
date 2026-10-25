using GameFlow.Infrastructure.Runtime.HidMaestro;
using GameFlow.Infrastructure.Runtime.Native;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime;

/// <summary>Creates the platform output sink. Activation failures remain visible.</summary>
public sealed class DefaultOutputSinkFactory(
    IServiceProvider serviceProvider,
    ILogger<DefaultOutputSinkFactory> logger) : IOutputSinkFactory
{
    private readonly IServiceProvider serviceProvider = serviceProvider;
    private readonly ILogger<DefaultOutputSinkFactory> logger = logger;

    public IOutputSink Create(string? providerId)
    {
        var requested = OutputProviderPolicy.Normalize(providerId);
        var resolved = OutputProviderPolicy.Resolve(providerId);

        if (OutputProviderPolicy.RequiresMigration(providerId))
        {
            logger.LogInformation(
                "Output provider '{Requested}' resolves to '{Resolved}' — {Reason}.",
                requested, resolved,
                OutputProviderPolicy.IsLegacyProviderId(requested)
                    ? "the requested backend was retired from this build"
                    : "it is not an output backend this build ships on this platform");
        }

        return resolved switch
        {
            OutputProviderPolicy.HidMaestro => ActivatorUtilities.CreateInstance<HidMaestroOutputSink>(serviceProvider),
            OutputProviderPolicy.NativeGamepad => ActivatorUtilities.CreateInstance<NativeGamepadOutputSink>(serviceProvider),
            _ => ActivatorUtilities.CreateInstance<PreviewOutputSink>(serviceProvider),
        };
    }

    /// <inheritdoc />
    public IOutputSink CreateNoOp() =>
        ActivatorUtilities.CreateInstance<PreviewOutputSink>(serviceProvider);
}
