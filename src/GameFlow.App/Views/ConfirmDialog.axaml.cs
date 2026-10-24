using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace GameFlow.App.Views;

/// <summary>Asks before a destructive action. See <see cref="AskAsync"/>.</summary>
public partial class ConfirmDialog : Window
{
    private bool confirmed;

    public ConfirmDialog()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <summary>
    /// Shows the question over <paramref name="owner"/> and returns true only
    /// when the user chose <paramref name="confirmLabel"/>. Closing the window
    /// or pressing Escape answers no.
    /// </summary>
    public static async Task<bool> AskAsync(Window owner, string heading, string message, string confirmLabel)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dialog = new ConfirmDialog { Title = heading };
        dialog.FindControl<TextBlock>("HeadingText")!.Text = heading;
        dialog.FindControl<TextBlock>("MessageText")!.Text = message;
        dialog.FindControl<Button>("ConfirmButton")!.Content = confirmLabel;
        await dialog.ShowDialog(owner);
        return dialog.confirmed;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        confirmed = true;
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
