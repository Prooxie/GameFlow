using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Localization;
using GameFlow.Infrastructure.Profiles;
using GameFlow.Infrastructure.Requirements;
using GameFlow.Infrastructure.Runtime;
using GameFlow.Infrastructure.Updates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GameFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAutofireInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        _ = services.Configure<AppRuntimeOptions>(configuration.GetSection("Runtime"));
        _ = services.Configure<Overlay.OverlayOptions>(configuration.GetSection("Overlay"));
        _ = services.AddMemoryCache();
        _ = services.AddPortableObjectLocalization(options => options.ResourcesPath = "Localization");

        _ = services.AddSingleton<IProfileRepository, JsonProfileRepository>();
        _ = services.AddSingleton<ProfileSession>();

        // The user-settings service depends on ILogLevelSwitch, which the App
        // layer registers from HostBuilderFactory after wiring it into the
        // Serilog config. Tests that need to resolve IUserSettingsService
        // without the App must register their own ILogLevelSwitch first.
        _ = services.AddSingleton<IUserSettingsService, UserSettingsService>();

        _ = services.AddSingleton<ILocalizationService, LocalizationService>();

        _ = services.AddSingleton<RuntimeSnapshotStore>();
        _ = services.AddSingleton<InputDeviceCatalog>();
        _ = services.AddSingleton<Runtime.Templates.DeviceTemplateStore>();
        _ = services.AddSingleton<Runtime.Input.ButtonMapStore>();
        if (OperatingSystem.IsWindows())
        {
            _ = services.AddSingleton<Runtime.Input.WindowsRawInputReader>();
            _ = services.AddSingleton<Runtime.Input.IKeyboardStateSource>(sp => sp.GetRequiredService<Runtime.Input.WindowsRawInputReader>());
            _ = services.AddSingleton<Runtime.Input.IMouseStateSource>(sp => sp.GetRequiredService<Runtime.Input.WindowsRawInputReader>());
            _ = services.AddSingleton<Runtime.Input.IRawInputAttacher>(sp => sp.GetRequiredService<Runtime.Input.WindowsRawInputReader>());
        }
        else
        {
            _ = services.AddSingleton<Runtime.Input.IKeyboardStateSource, Runtime.Input.NullKeyboardStateSource>();
            _ = services.AddSingleton<Runtime.Input.IMouseStateSource, Runtime.Input.NullMouseStateSource>();
            _ = services.AddSingleton<Runtime.Input.IRawInputAttacher, Runtime.Input.NullRawInputAttacher>();
        }
        _ = services.AddSingleton<Runtime.Slots.SlotRegistry>();
        _ = services.AddSingleton<Runtime.Slots.SlotSnapshotStore>();
        _ = services.AddSingleton<IInputSourceFactory, DefaultInputSourceFactory>();
        _ = services.AddSingleton<IOutputSinkFactory, DefaultOutputSinkFactory>();
        _ = services.AddHostedService<RuntimeCoordinator>();
        _ = services.AddHostedService<RawInputEnumerationService>();
        _ = services.AddHostedService<Overlay.OverlayServer>();

        // Step 3 of the roadmap: requirement & update checks.
        _ = services.AddSingleton<IRequirementChecker, DefaultRequirementChecker>();
        _ = services.AddSingleton<IUpdateChecker, GitHubUpdateChecker>();
        _ = services.AddSingleton<IUpdateInstaller, DefaultUpdateInstaller>();

        return services;
    }
}
