using GameFlow.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Serilog;

namespace GameFlow.App.Views;

/// <summary>
/// Code-behind for the Options / Settings dialog.
///
/// <para>
/// Owns three small responsibilities:
/// <list type="bullet">
///   <item><description>Wiring the folder pickers (Profiles dir, Logs dir).</description></item>
///   <item><description>Routing the Apply button through the
///     <see cref="SettingsDialogViewModel.ApplyAsync"/> command and
///     closing on success.</description></item>
///   <item><description>Routing the Cancel button to discard pending
///     edits via <see cref="SettingsDialogViewModel.Reload"/> and
///     close.</description></item>
/// </list>
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
        Closed += OnClosed;
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
                Title = button.Content?.ToString() ?? "Select folder",
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
