using Autofire.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Serilog;

namespace Autofire.App.Views;

public partial class ShellWindow : Window
{
    private readonly DispatcherTimer refreshTimer;
    private ShellViewModel? shellViewModel;
    private bool isRefreshing;
    private bool isClosing;

    public ShellWindow()
    {
        InitializeComponent();

        refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)   // ~30 Hz UI tick
        };

        refreshTimer.Tick += RefreshTimerOnTick;
        Opened  += OnOpened;
        Closing += OnClosing;
        Closed  += OnClosed;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        shellViewModel?.ControlMappingRequested -= OnControlMappingRequested;
        base.OnDataContextChanged(e);
        shellViewModel = DataContext as ShellViewModel;
        shellViewModel?.ControlMappingRequested += OnControlMappingRequested;
    }

    private async void OnControlMappingRequested(object? sender, ControlMappingRequestedEventArgs e)
    {
        if (isClosing)
        {
            return;
        }

        var window = new ControlMappingWindow
        {
            DataContext = e.DialogViewModel
        };

        try
        {
            await window.ShowDialog(this);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Control mapping window failed to open.");
            e.DialogViewModel.Dispose();
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (!isClosing)
        {
            refreshTimer.Start();
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        isClosing = true;
        refreshTimer.Stop();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        refreshTimer.Stop();
        refreshTimer.Tick -= RefreshTimerOnTick;
        Opened  -= OnOpened;
        Closing -= OnClosing;
        Closed  -= OnClosed;
        shellViewModel?.ControlMappingRequested -= OnControlMappingRequested;
        shellViewModel = null;

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private async void RefreshTimerOnTick(object? sender, EventArgs e)
    {
        if (isClosing || isRefreshing || DataContext is not ShellViewModel viewModel)
        {
            return;
        }

        try
        {
            isRefreshing = true;
            await viewModel.RefreshRuntimeAsync();
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException)
        {
            refreshTimer.Stop();
        }
        catch (InvalidOperationException exception) when (isClosing)
        {
            Log.Debug(exception, "Dashboard refresh stopped because the shell window is closing.");
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Dashboard refresh failed.");
        }
        finally
        {
            isRefreshing = false;
        }
    }
}
