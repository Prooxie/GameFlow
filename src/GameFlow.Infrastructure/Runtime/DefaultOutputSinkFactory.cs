using GameFlow.Infrastructure.Runtime.HidMaestro;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime;

/// <summary>
/// Creates output sinks per <see cref="OutputProviderPolicy"/>: on
/// Windows every provider id — current, legacy, empty, or unknown —
/// resolves to the HIDMaestro sink, because HIDMaestro is the only
/// output backend this build ships. On non-Windows platforms everything
/// resolves to the in-app preview sink (HIDMaestro is Windows-only).
///
/// <para>
/// This is deliberately unconditional rather than a switch over ids:
/// the previous factory routed unknown ids (including retired
/// <c>vigem-*</c> values still sitting in old profiles, and the old
/// <c>"preview"</c> default) to a silent preview fallback — which is
/// exactly how "I have a profile, input works, and yet no virtual
/// controller ever appears" happened. Now the only way to get no real
/// device on Windows is HIDMaestro itself failing to activate, and the
/// sink reports that loudly instead of substituting anything.
/// </para>
/// </summary>
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

        if (OutputProviderPolicy.RequiresMigration(providerId, OperatingSystem.IsWindows()))
        {
            logger.LogInformation(
                "Output provider '{Requested}' resolves to '{Resolved}' — {Reason}.",
                requested, resolved,
                OutputProviderPolicy.IsLegacyProviderId(requested)
                    ? "the requested backend was retired from this build"
                    : "it is not an output backend this build ships on this platform");
        }

        return resolved == OutputProviderPolicy.HidMaestro
            ? ActivatorUtilities.CreateInstance<HidMaestroOutputSink>(serviceProvider)
            : ActivatorUtilities.CreateInstance<PreviewOutputSink>(serviceProvider);
    }

    /// <inheritdoc />
    public IOutputSink CreateNoOp() =>
        ActivatorUtilities.CreateInstance<PreviewOutputSink>(serviceProvider);
}
