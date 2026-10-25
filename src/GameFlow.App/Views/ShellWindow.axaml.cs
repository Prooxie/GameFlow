using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using GameFlow.App.Services;
using GameFlow.App.ViewModels;
using Serilog;

namespace GameFlow.App.Views;

public partial class ShellWindow : Window
{
    private readonly DashboardFrameClock frameClock = new();
    private readonly Action<TimeSpan> refreshFrame;

    private ShellViewModel? shellViewModel;
    private bool isClosing;

    /// <summary>
    /// Container used to build the short-lived view-models behind the
    /// sheets this window opens (setup walkthrough, phone controller).
    /// </summary>
    /// <remarks>
    /// Null only under the XAML previewer, which uses the parameterless
    /// constructor; at runtime the window is always resolved from DI, and
    /// the greedier constructor wins.
    /// </remarks>
    private readonly IServiceProvider? services;

    public ShellWindow() : this(null)
    {
    }

    public ShellWindow(IServiceProvider? services)
    {
        this.services = services;
        InitializeComponent();

        refreshFrame = RefreshFrame;
        Opened += OnOpened;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Keeps the first frame inside the primary monitor's usable area,
    /// including DPI scaling and the taskbar. This runs before
    /// <see cref="Window.Show()"/> so an oversized window never flashes.
    /// </summary>
    public void FitToWorkingArea()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var scaling = screen.Scaling > 0 ? screen.Scaling : 1d;
        var maximumWidth = screen.WorkingArea.Width / scaling * 0.92d;
        var maximumHeight = screen.WorkingArea.Height / scaling * 0.92d;

        MinWidth = Math.Min(MinWidth, maximumWidth);
        MinHeight = Math.Min(MinHeight, maximumHeight);
        Width = Math.Min(Width, maximumWidth);
        Height = Math.Min(Height, maximumHeight);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        // Explicit null checks rather than `shellViewModel?.Event -= handler`:
        // the null-conditional operator cannot be used for event
        // subscription in C# (the left side of += / -= must be an event
        // access, not a null-conditional expression). Behaviour is
        // identical — subscribe/unsubscribe only when the view model is
        // present.
        if (shellViewModel is not null)
        {
            shellViewModel.ControlMappingRequested -= OnControlMappingRequested;
            shellViewModel.DeviceSettingsRequested -= OnDeviceSettingsRequested;
            shellViewModel.WalkthroughRequested -= OnWalkthroughRequested;
            shellViewModel.PhoneControllerRequested -= OnPhoneControllerRequested;
            shellViewModel.OverlayUrlCopyRequested -= OnOverlayUrlCopyRequested;
            shellViewModel.KeyboardMouseMapRequested -= OnKeyboardMouseMapRequested;
        }

        base.OnDataContextChanged(e);
        shellViewModel = DataContext as ShellViewModel;

        if (shellViewModel is not null)
        {
            shellViewModel.ControlMappingRequested += OnControlMappingRequested;
            shellViewModel.DeviceSettingsRequested += OnDeviceSettingsRequested;
            shellViewModel.WalkthroughRequested += OnWalkthroughRequested;
            shellViewModel.PhoneControllerRequested += OnPhoneControllerRequested;
            shellViewModel.OverlayUrlCopyRequested += OnOverlayUrlCopyRequested;
            shellViewModel.KeyboardMouseMapRequested += OnKeyboardMouseMapRequested;
        }
    }

    /// <summary>Opens the keyboard &amp; mouse controller map editor.</summary>
    private async void OnKeyboardMouseMapRequested(object? sender, KeyboardMouseMapRequest request)
    {
        if (isClosing || services?.GetService(typeof(KeyboardMouseMapEditorViewModel)) is not KeyboardMouseMapEditorViewModel editor)
        {
            return;
        }

        try
        {
            var selected = shellViewModel?.DevicesPanel.SelectedDevice;
            editor.SelectDevice(request.DeviceId ?? (selected?.Category is GameFlow.Infrastructure.Runtime.DeviceCategory.Keyboard or GameFlow.Infrastructure.Runtime.DeviceCategory.Mouse ? selected.Id : null));
            editor.PrepareInput(request.Key, request.Mouse);
            await new KeyboardMouseMapWindow { DataContext = editor }.ShowDialog(this);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Keyboard & mouse map editor failed to open.");
        }
    }

    /// <summary>Sidebar "Setup guide" — opens the walkthrough on demand.</summary>
    private async void OnWalkthroughRequested(object? sender, EventArgs e)
    {
        if (isClosing || services is null)
        {
            return;
        }

        try
        {
            var window = new SetupWalkthroughWindow
            {
                DataContext = services.GetService(typeof(SetupWalkthroughViewModel)),
            };
            await window.ShowDialog(this);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Setup walkthrough failed to open from the sidebar.");
        }
    }

    /// <summary>Sidebar trash button on a virtual controller: asks, then deletes it.</summary>
    private async void OnDeleteVirtualControllerClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: MenuColumnItemViewModel { DeleteCommand: { } delete } row })
        {
            return;
        }

        try
        {
            if (await ConfirmDialog.AskAsync(this, UiTextResources.Format("UiDeleteName", row.Name), row.DeleteConfirmText, UiTextResources.Get("UiDelete")))
            {
                delete.Execute(null);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Deleting a virtual controller from the sidebar failed.");
        }
    }

    /// <summary>The virtual controller editor's Delete: asks, then deletes the selected controller.</summary>
    private async void OnDeleteSelectedSlotClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var slots = shellViewModel?.SlotsPanel;
        if (slots?.SelectedSlot is not { } slot)
        {
            return;
        }

        try
        {
            var name = string.IsNullOrWhiteSpace(slot.Name) ? "this virtual controller" : slot.Name;
            if (await ConfirmDialog.AskAsync(
                    this,
                    UiTextResources.Format("UiDeleteName", name),
                    UiTextResources.Get("UiDeleteVirtual"),
                    UiTextResources.Get("UiDelete")))
            {
                slots.DeleteSlotById(slot.Id);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Deleting a virtual controller failed.");
        }
    }

    /// <summary>Devices page "Remove phone": asks, then removes the phone and revokes its link.</summary>
    private async void OnRemovePhoneClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Control { Tag: string deviceId } || shellViewModel is null)
        {
            return;
        }

        try
        {
            if (await ConfirmDialog.AskAsync(
                    this,
                    UiTextResources.Get("UiRemovePhoneTitle"),
                    UiTextResources.Get("UiRemovePhoneMessage"),
                    UiTextResources.Get("UiRemove")))
            {
                shellViewModel.RemovePhoneCommand.Execute(deviceId);
            }
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Removing a phone failed.");
        }
    }

    /// <summary>Opens one phone's sheet: name, QR code of its own link, connection state.</summary>
    private async void OnPhoneControllerRequested(object? sender, int seat)
    {
        if (isClosing
            || services?.GetService(typeof(OverlayPanelViewModel)) is not OverlayPanelViewModel network
            || services.GetService(typeof(GameFlow.Infrastructure.Runtime.Web.PhoneRegistry)) is not GameFlow.Infrastructure.Runtime.Web.PhoneRegistry registry
            || services.GetService(typeof(GameFlow.Infrastructure.Runtime.Web.WebControllerHub)) is not GameFlow.Infrastructure.Runtime.Web.WebControllerHub hub
            || services.GetService(typeof(GameFlow.Infrastructure.Runtime.Web.WebControllerServer)) is not GameFlow.Infrastructure.Runtime.Web.WebControllerServer server)
        {
            return;
        }

        try
        {
            var window = new PhoneControllerWindow
            {
                DataContext = new PhoneSheetViewModel(network, registry, hub, server, seat),
            };
            await window.ShowDialog(this);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Phone controller sheet failed to open.");
        }
    }

    /// <summary>
    /// A dashboard panel's "Copy OBS link" — puts that surface's overlay URL
    /// on the clipboard. The panel built the URL; only the window owns the
    /// clipboard.
    /// </summary>
    private async void OnOverlayUrlCopyRequested(object? sender, string url)
    {
        if (GetTopLevel(this)?.Clipboard is not { } clipboard)
        {
            return;
        }

        if (string.IsNullOrEmpty(url))
        {
            shellViewModel?.ReportStatus("The web server is not running, so there is no overlay URL yet.");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(url);
            shellViewModel?.ReportStatus("OBS link copied — add a Browser source in OBS and paste it as the URL.");
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Copying the overlay URL from the dashboard failed.");
        }
    }

    /// <summary>
    /// Click on a slot's VIRTUAL panel opens that slot's tuning editor.
    /// The slot id rides on the Border's Tag, so the handler doesn't need
    /// to walk the visual tree to work out which panel was hit.
    /// </summary>
    private void OnVirtualPanelPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (GameFlow.App.Services.ControllerPanelInput.IsControlInteraction(e.Source)) { return; }
        if (sender is not Border { Tag: string slotId } || string.IsNullOrWhiteSpace(slotId))
        {
            return;
        }

        // Left button only — a right-click here shouldn't hijack any
        // future context menu on the panel.
        if (!e.GetCurrentPoint(sender as Control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        (DataContext as ShellViewModel)?.OpenDeviceSettingsCommand.Execute(slotId);
    }

    private void OnVirtualPanelKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (GameFlow.App.Services.ControllerPanelInput.IsControlInteraction(e.Source)) { return; }
        if (e.Key is Avalonia.Input.Key.Enter or Avalonia.Input.Key.Space && sender is Border { Tag: string slotId })
        {
            (DataContext as ShellViewModel)?.OpenDeviceSettingsCommand.Execute(slotId);
            e.Handled = true;
        }
    }

    private async void OnDeviceSettingsRequested(object? sender, DeviceSettingsRequestedEventArgs e)
    {
        if (isClosing)
        {
            return;
        }

        var window = new DeviceSettingsWindow { DataContext = e.EditorViewModel };
        try
        {
            await window.ShowDialog(this);
        }
        catch (Exception exception)
        {
            // Settings persist on every change, so a failure to SHOW the
            // dialog costs nothing already saved — log and carry on.
            Log.Error(exception, "Device settings window failed to open.");
        }
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
            RequestAnimationFrame(refreshFrame);
        }
        // Attach the Raw Input reader to this window's HWND so the keyboard
        // + mouse subsystem starts receiving WM_INPUT. No-op off Windows.
        try
        {
            var handle = TryGetPlatformHandle();
            if (handle is not null && shellViewModel is not null)
            {
                shellViewModel.AttachRawInput(handle.Handle);
            }
        }
        catch (Exception ex)
        {
            Log.ForContext<ShellWindow>().Warning(ex, "Raw Input attach failed.");
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        isClosing = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        Closing -= OnClosing;
        Closed -= OnClosed;
        if (shellViewModel is not null)
        {
            // Same reason as OnDataContextChanged above: `?.` is not
            // valid for event unsubscription.
            shellViewModel.ControlMappingRequested -= OnControlMappingRequested;
            // Subscribed alongside the above in OnDataContextChanged, so
            // it has to be released here too — otherwise the shell view
            // model keeps this window alive after it closes.
            shellViewModel.DeviceSettingsRequested -= OnDeviceSettingsRequested;
            shellViewModel.WalkthroughRequested -= OnWalkthroughRequested;
            shellViewModel.PhoneControllerRequested -= OnPhoneControllerRequested;
            shellViewModel.OverlayUrlCopyRequested -= OnOverlayUrlCopyRequested;
            shellViewModel.KeyboardMouseMapRequested -= OnKeyboardMouseMapRequested;
        }
        shellViewModel = null;

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    /// <summary>Reads the latest state on a display frame. Only one refresh can remain active.</summary>
    private async void RefreshFrame(TimeSpan timestamp)
    {
        if (isClosing) { return; }
        try
        {
            if (DataContext is ShellViewModel viewModel
                && frameClock.ShouldRefresh(timestamp, viewModel.DashboardRefreshHz))
            {
                await viewModel.RefreshRuntimeAsync();
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) when (isClosing) { }
        catch (Exception exception)
        {
            Log.Error(exception, "Dashboard refresh failed.");
        }
        finally
        {
            if (!isClosing) { RequestAnimationFrame(refreshFrame); }
        }
    }
}
