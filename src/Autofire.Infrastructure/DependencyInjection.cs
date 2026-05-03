using Autofire.Infrastructure.Configuration;
using Autofire.Infrastructure.Localization;
using Autofire.Infrastructure.Profiles;
using Autofire.Infrastructure.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Autofire.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofireInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        _ = services.Configure<AppRuntimeOptions>(configuration.GetSection("Runtime"));
        _ = services.AddMemoryCache();
        _ = services.AddPortableObjectLocalization(options => options.ResourcesPath = "Localization");

        _ = services.AddSingleton<IProfileRepository, JsonProfileRepository>();
        _ = services.AddSingleton<ProfileSession>();

        _ = services.AddSingleton<ILocalizationService, LocalizationService>();

        _ = services.AddSingleton<RuntimeSnapshotStore>();
        _ = services.AddSingleton<InputDeviceCatalog>();
        _ = services.AddSingleton<IInputSourceFactory, DefaultInputSourceFactory>();
        _ = services.AddSingleton<IOutputSinkFactory, DefaultOutputSinkFactory>();
        _ = services.AddHostedService<RuntimeCoordinator>();

        return services;
    }
}
