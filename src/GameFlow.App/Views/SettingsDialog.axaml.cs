using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using GameFlow.App.ViewModels;
using Serilog;

namespace GameFlow.App.Views;

/// <summary>
/// Code-behind for the Options / Settings dialog.
///
/// <para>
/// Owns a few small responsibilities:
/// <list type="bullet">
///   <item><description>Wiring the folder pickers (Profiles dir, Logs dir).</description></item>
///   <item><description>Opening the setup walkthrough over the dialog.</description></item>
///   <item><description>Ctrl+Tab / Ctrl+PageDown switching between the
///     dialog's tabs.</description></item>
///   <item><description>Routing the Apply button through the
///     <see cref="SettingsDialogViewModel.ApplyAsync"/> command and
///     closing on success.</description></item>
///   <item><description>Routing the Cancel button to discard pending
///     edits via <see cref="SettingsDialogViewModel.Reload"/> and
///     close.</description></item>
/// </list>
/// </para>
///
/// <para>
/// There is no URL handling here any more. The OBS overlay link moved onto
/// each controller panel on the dashboard, and phones are added one at a
/// time from Devices, each with its own link — so the Copy / Open buttons
/// and the clipboard and browser helpers behind them went with them.
/// </para>
/// </summary>
public partial class SettingsDialog : Window
{
    /// <summary>
    /// XAML loader entry point. The dialog expects its
    /// <see cref="Avalonia.StyledElement.DataContext"/> to be populated with a
    /// <see cref="SettingsDialogViewModel"/> by the caller before
    /// <see cref="Window.ShowDialog"/>.
    /// </summary>
    public SettingsDialog()
    {
        InitializeComponent();
        Opened += (_, _) => WindowSizing.Fit(this);
        Opened += OnOpened;
        Closed += OnClosed;

        // Tunnel, not the ordinary bubble: Tab is consumed by the window's
        // keyboard-navigation handler on the way back UP, Ctrl held or not,
        // so a bubbling handler would never see Ctrl+Tab at all.
        AddHandler(KeyDownEvent, OnTabSwitchKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Kicks off the firewall check once the dialog is on screen.
    /// </summary>
    /// <remarks>
    /// Deliberately fire-and-forget rather than awaited before showing:
    /// the check shells out to netsh, and the dialog should not wait on a
    /// process launch to appear. The banner it feeds is hidden until the
    /// answer arrives, so a slow or failed check simply shows nothing
    /// rather than a wrong reassurance.
    /// </remarks>
    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is SettingsDialogViewModel vm)
        {
            _ = vm.Overlay.RefreshReachabilityAsync();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Detach the VM's <c>CultureChanged</c> subscription when the
    /// dialog closes so the long-lived localization service doesn't
    /// hold this short-lived view-model alive via the event handler.
    /// </summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        if (DataContext is SettingsDialogViewModel vm)
        {
            vm.Dispose();
        }
    }

    /// <summary>
    /// Ctrl+Tab / Ctrl+Shift+Tab and Ctrl+PageDown / Ctrl+PageUp move to
    /// the next / previous tab, wrapping at either end.
    /// </summary>
    /// <remarks>
    /// The standard keys for a Windows tabbed dialog. Without them a
    /// keyboard user has to Shift+Tab back through every control on the
    /// page to reach the tab list. Focus moves onto the newly selected tab
    /// header, because the control that had it was on the page that just
    /// left the visual tree, and focus left there goes nowhere.
    /// </remarks>
    private void OnTabSwitchKeyDown(object? sender, KeyEventArgs e)
    {
        if ((e.KeyModifiers & KeyModifiers.Control) == 0)
        {
            return;
        }

        var step = e.Key switch
        {
            Key.Tab => (e.KeyModifiers & KeyModifiers.Shift) == 0 ? 1 : -1,
            Key.PageDown => 1,
            Key.PageUp => -1,
            _ => 0,
        };

        var tabs = this.FindControl<TabControl>("SettingsTabs");
        if (step == 0 || tabs is null || tabs.ItemCount == 0)
        {
            return;
        }

        var count = tabs.ItemCount;
        var next = ((Math.Max(tabs.SelectedIndex, 0) + step) % count + count) % count;
        tabs.SelectedIndex = next;
        (tabs.ContainerFromIndex(next) as Control)?.Focus(NavigationMethod.Directional);
        e.Handled = true;
    }

    /// <summary>
    /// Re-runs the first-run walkthrough on demand.
    /// </summary>
    /// <remarks>
    /// Parented to this dialog rather than the shell, so it behaves like
    /// the modal it is and returns here when closed. The view-model is
    /// resolved through the shell's own service provider: the walkthrough
    /// needs the live device catalog and slot registry, and re-creating
    /// either would give it a private, empty view of the machine.
    /// </remarks>
    private async void OnRunWalkthrough(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsDialogViewModel vm)
        {
            return;
        }

        try
        {
            var walkthrough = vm.CreateWalkthrough();
            var window = new SetupWalkthroughWindow { DataContext = walkthrough };
            await window.ShowDialog(this);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Settings: could not open the setup walkthrough.");
            vm.StatusMessage = UiTextResources.Get("UiSetupOpenFailed");
        }
    }

    /// <summary>
    /// Opens a folder picker and writes the chosen path into the
    /// matching textbox via the view-model. The button's <c>Tag</c>
    /// distinguishes which override to set ("profiles" or "logs").
    /// </summary>
    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || DataContext is not SettingsDialogViewModel vm)
        {
            return;
        }

        var which = button.Tag as string ?? "";

        IReadOnlyList<IStorageFolder> folders;
        try
        {
            folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                AllowMultiple = false,
                Title = button.Content?.ToString() ?? UiTextResources.Get("UiSelectFolder"),
            });
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Folder picker failed.");
            return;
        }

        if (folders.Count == 0)
        {
            return;
        }

        var chosen = folders[0].Path.LocalPath;
        if (string.IsNullOrEmpty(chosen))
        {
            return;
        }

        switch (which)
        {
            case "profiles":
                vm.ProfilesDirectoryOverride = chosen;
                break;
            case "logs":
                vm.LogsDirectoryOverride = chosen;
                break;
        }
    }

    /// <summary>
    /// Apply: persist the dialog state via the view-model. Closes only
    /// if the apply succeeded; on failure the status line shows the
    /// error and the dialog stays open so the user can correct things.
    /// </summary>
    private async void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsDialogViewModel vm)
        {
            Close();
            return;
        }

        var ok = await vm.ApplyAsync();
        if (ok)
        {
            Close();
        }
    }

    /// <summary>
    /// Cancel: drop any pending edits by reloading from the persisted
    /// snapshot, then close. <see cref="Button.IsCancel"/> additionally
    /// makes Esc trigger this.
    /// </summary>
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsDialogViewModel vm)
        {
            vm.Reload();
        }
        Close();
    }
}
