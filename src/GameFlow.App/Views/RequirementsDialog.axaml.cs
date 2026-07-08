using System.Diagnostics;
using GameFlow.Infrastructure.Requirements;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Serilog;

namespace GameFlow.App.Views;

/// <summary>
/// Dialog that lists missing requirements (one card per item) and lets
/// the user open each requirement's install page, opt out of the
/// startup check, or close the dialog.
///
/// <para>
/// Code-behind only — the surface area is small enough that a separate
/// view-model would be over-engineering. The hosting coordinator
/// pre-filters the list to applicable + unsatisfied items before
/// constructing the dialog, so the items shown are exactly the ones
/// the user can act on.
/// </para>
/// </summary>
public partial class RequirementsDialog : Window
{
    /// <summary>
    /// True when the user ticked "Don't check at startup again". The
    /// hosting coordinator reads this on close and persists it to
    /// <c>AppSettings.CheckRequirementsOnStartup</c>.
    /// </summary>
    public bool DontAskAgain { get; private set; }

    /// <summary>
    /// XAML loader entry point. The actual <c>Items</c> binding happens
    /// after construction via <see cref="SetRequirements"/>.
    /// </summary>
    public RequirementsDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>
    /// Populates the list shown in the dialog. Call this exactly once
    /// before <see cref="Window.ShowDialog"/>.
    /// </summary>
    public void SetRequirements(IReadOnlyList<RequirementStatus> requirements)
    {
        var list = this.FindControl<ItemsControl>("RequirementsList");
        if (list is not null)
        {
            list.ItemsSource = requirements;
        }
    }

    /// <summary>
    /// Handles per-requirement "Open install page" clicks. Opens the
    /// installer URL in the default browser. Failures are logged at
    /// Warning and the dialog stays open.
    /// </summary>
    private void OnOpenInstallPageClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not RequirementStatus requirement)
        {
            return;
        }

        if (requirement.InstallerUrl is null)
        {
            Log.Debug("Requirement {RequirementId} has no installer URL — ignoring click.", requirement.Id);
            return;
        }

        try
        {
            _ = Process.Start(new ProcessStartInfo
            {
                FileName = requirement.InstallerUrl.ToString(),
                UseShellExecute = true,
            });
            Log.Information(
                "Opened installer URL {Url} for requirement {RequirementId}.",
                requirement.InstallerUrl,
                requirement.Id);
        }
        catch (Exception exception)
        {
            Log.Warning(
                exception,
                "Could not open installer URL {Url} for requirement {RequirementId}.",
                requirement.InstallerUrl,
                requirement.Id);
        }
    }

    /// <summary>
    /// Default "Close" button handler.
    /// </summary>
    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        CaptureCheckboxAndClose();
    }

    /// <summary>
    /// Reads the "don't ask again" checkbox into <see cref="DontAskAgain"/>
    /// and closes the dialog.
    /// </summary>
    private void CaptureCheckboxAndClose()
    {
        var checkbox = this.FindControl<CheckBox>("DontAskAgainCheckbox");
        DontAskAgain = checkbox?.IsChecked == true;
        Close();
    }
}
