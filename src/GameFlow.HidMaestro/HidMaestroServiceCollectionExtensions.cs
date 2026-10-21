using GameFlow.Infrastructure.Runtime.HidMaestro;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GameFlow.HidMaestro;

/// <summary>
/// Registers the HIDMaestro adapter. The app calls this on every platform, so
/// this type carries no platform attribute. The Windows check is inside.
/// </summary>
public static class HidMaestroServiceCollectionExtensions
{
    /// <summary>
    /// On Windows, replaces the unavailable backend that Infrastructure
    /// registers with <see cref="HidMaestroBackend"/>. Elsewhere it does
    /// nothing. Call it after <c>AddGameFlowInfrastructure</c>.
    /// </summary>
    /// <remarks>
    /// Registering the backend does not touch the SDK. The backend searches
    /// for it on the first status call.
    /// </remarks>
    public static IServiceCollection AddGameFlowHidMaestro(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (OperatingSystem.IsWindows())
        {
            _ = services.Replace(ServiceDescriptor.Singleton<IHidMaestroBackend, HidMaestroBackend>());
        }

        return services;
    }
}
