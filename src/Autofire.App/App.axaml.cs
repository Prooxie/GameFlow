using Autofire.App.Bootstrap;
using Autofire.App.ViewModels;
using Autofire.App.Views;
using Autofire.Infrastructure.Localization;
using Autofire.Infrastructure.Profiles;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Autofire.App;

public partial class App : Application
{
    private IHost? host;
    private int shutdownStarted;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splashWindow = new SplashWindow();
            desktop.MainWindow = splashWindow;
            splashWindow.Show();

            _ = StartDesktopAsync(desktop, splashWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task StartDesktopAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splashWindow)
    {
        try
        {
            host = HostBuilderFactory.Create(Environment.GetCommandLineArgs()).Build();
            await host.StartAsync();

            var profileSession = host.Services.GetRequiredService<ProfileSession>();
            await profileSession.EnsureInitializedAsync();

            var localization = host.Services.GetRequiredService<ILocalizationService>();
            localization.SetCulture(profileSession.Settings.SelectedCulture);

            var shellViewModel = host.Services.GetRequiredService<ShellViewModel>();
            await shellViewModel.InitializeAsync();

            await Task.Delay(TimeSpan.FromMilliseconds(350));

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var shellWindow = host!.Services.GetRequiredService<ShellWindow>();
                shellWindow.DataContext = shellViewModel;

                desktop.MainWindow = shellWindow;
                shellWindow.Show();
                splashWindow.Close();

                desktop.Exit += DesktopOnExit;
            });
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Application startup was cancelled.");
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Desktop bootstrap failed.");

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                splashWindow.Content = new TextBlock
                {
                    Text = exception.Message,
                    Margin = new Thickness(24)
                };
            });
        }
    }

    private void DesktopOnExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (Interlocked.Exchange(ref shutdownStarted, 1) == 1)
        {
            return;
        }

        var currentHost = Interlocked.Exchange(ref host, null);
        if (currentHost is null)
        {
            return;
        }

        try
        {
            currentHost.StopAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Host shutdown was cancelled during application exit.");
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException or ObjectDisposedException))
        {
            Log.Debug("Host shutdown ended with cancellation or disposal during application exit.");
        }
        catch (ObjectDisposedException)
        {
            Log.Debug("Host was already disposed during application exit.");
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Failed to stop host cleanly.");
        }
        finally
        {
            try
            {
                currentHost.Dispose();
            }
            catch (Exception exception)
            {
                Log.Debug(exception, "Host dispose reported an error during application exit.");
            }
        }
    }
}
