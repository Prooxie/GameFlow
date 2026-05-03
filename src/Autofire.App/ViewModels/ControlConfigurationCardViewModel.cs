namespace Autofire.App.ViewModels;

public sealed record ControlConfigurationEntryViewModel(string Label, string Value, string AccentColor);

public sealed record ControlConfigurationCardViewModel(
    string SelectionKey,
    string Title,
    string Subtitle,
    IReadOnlyList<ControlConfigurationEntryViewModel> Entries)
{
    public string RuleCountText => Entries.Count switch
    {
        0 => "No rules",
        1 => "1 rule",
        _ => $"{Entries.Count} rules"
    };
}
