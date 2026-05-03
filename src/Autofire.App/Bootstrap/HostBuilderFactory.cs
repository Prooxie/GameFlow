using Autofire.App.Services;
using Autofire.App.ViewModels;
using Autofire.App.Views;
using Autofire.Infrastructure;
using Autofire.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace Autofire.App.Bootstrap;

public static class HostBuilderFactory
{
    public static IHostBuilder Create(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, configurationBuilder) =>
            {
                _ = configurationBuilder.SetBasePath(AppContext.BaseDirectory);
                _ = configurationBuilder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                _ = configurationBuilder.AddEnvironmentVariables(prefix: "AUTOFIRE_");
            })
            .UseSerilog((hostingContext, services, loggerConfiguration) =>
            {
                _ = Directory.CreateDirectory(AppPaths.LogsDirectory);

                _ = loggerConfiguration
                    .ReadFrom.Configuration(hostingContext.Configuration)
                    .ReadFrom.Services(services)
                    .Enrich.FromLogContext()
                    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                    .WriteTo.Console()
                    .WriteTo.File(
                        Path.Combine(AppPaths.LogsDirectory, "autofire-next-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14);
            })
            .ConfigureServices((hostingContext, services) =>
            {
                _ = services.Configure<HostOptions>(options =>
                {
                    options.ShutdownTimeout = TimeSpan.FromSeconds(5);
                });

                _ = services.AddAutofireInfrastructure(hostingContext.Configuration);
                _ = services.AddSingleton<IProfileFileDialogService, ProfileFileDialogService>();
                _ = services.AddSingleton<ShellViewModel>();
                _ = services.AddSingleton<ShellWindow>();
            });
    }
}
