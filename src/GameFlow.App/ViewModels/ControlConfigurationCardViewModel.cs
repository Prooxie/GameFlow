namespace GameFlow.App.ViewModels;

public sealed record ControlConfigurationEntryViewModel(string Label, string Value, string AccentColor);

public sealed record ControlConfigurationCardViewModel(
    string SelectionKey,
    string Title,
    string Subtitle,
    string RuleCountText,
    IReadOnlyList<ControlConfigurationEntryViewModel> Entries);
