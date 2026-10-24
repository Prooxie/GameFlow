using GameFlow.Core.Models;
using Microsoft.Extensions.Logging;

namespace GameFlow.App.ViewModels;

public sealed class ControlMappingDialogViewModel : ViewModelBase, IDisposable
{
    private readonly IReadOnlyList<GameFlow.Core.Models.MappingRule> backgroundRules;

    public ControlMappingDialogViewModel(
        string selectionKey,
        ProfileDocument profile,
        ILoggerFactory loggerFactory)
    {
        SelectionKey = ControlRuleMatcher.EnsurePhysicalSelectionKey(selectionKey);
        IsVirtualTarget = SelectionKey.StartsWith("virtual:", StringComparison.OrdinalIgnoreCase);

        backgroundRules = [.. profile.Rules.Where(rule => !ControlRuleMatcher.Matches(SelectionKey, rule))];

        var focusedProfile = profile with
        {
            Rules = [.. profile.Rules.Where(rule => ControlRuleMatcher.Matches(SelectionKey, rule))]
        };

        Editor = new MappingEditorViewModel(loggerFactory.CreateLogger<MappingEditorViewModel>());
        Editor.LoadFromProfile(focusedProfile);
        Editor.SetPreferredSelection(SelectionKey);

        if (focusedProfile.Rules.Count == 0)
        {
            Editor.PreFillFromElement(SelectionKey);
        }

        Editor.RulesChanged += OnRulesChanged;
    }

    public event EventHandler<IReadOnlyList<GameFlow.Core.Models.MappingRule>>? MergedRulesChanged;

    public string SelectionKey { get; }

    public string SelectionTitle => ControlRuleMatcher.GetTitle(SelectionKey);

    public string SelectionHint => IsVirtualTarget
        ? UiTextResources.Format("UiChooseSource", SelectionTitle)
        : ControlRuleMatcher.GetHint(SelectionKey);

    public bool IsVirtualTarget { get; }

    public string SelectionScopeLabel => UiTextResources.Get(IsVirtualTarget ? "UiVirtualTarget" : "UiPhysicalInput");

    public MappingEditorViewModel Editor { get; }

    private void OnRulesChanged(object? sender, IReadOnlyList<GameFlow.Core.Models.MappingRule> focusedRules)
    {
        var merged = backgroundRules.Concat(focusedRules).ToArray();
        MergedRulesChanged?.Invoke(this, merged);
    }

    public void Dispose()
    {
        Editor.RulesChanged -= OnRulesChanged;
        Editor.Dispose();
    }
}
