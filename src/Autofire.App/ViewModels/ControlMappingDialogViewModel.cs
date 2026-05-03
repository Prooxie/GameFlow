using Autofire.Core.Models;
using Microsoft.Extensions.Logging;

namespace Autofire.App.ViewModels;

public sealed class ControlMappingDialogViewModel : ViewModelBase, IDisposable
{
    private readonly IReadOnlyList<Autofire.Core.Models.MappingRule> backgroundRules;

    public ControlMappingDialogViewModel(
        string selectionKey,
        ProfileDocument profile,
        ILoggerFactory loggerFactory)
    {
        SelectionKey = ControlRuleMatcher.EnsurePhysicalSelectionKey(selectionKey);
        SelectionTitle = ControlRuleMatcher.GetTitle(SelectionKey);
        SelectionHint = ControlRuleMatcher.GetHint(SelectionKey);

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

    public event EventHandler<IReadOnlyList<Autofire.Core.Models.MappingRule>>? MergedRulesChanged;

    public string SelectionKey { get; }

    public string SelectionTitle { get; }

    public string SelectionHint { get; }

    public MappingEditorViewModel Editor { get; }

    private void OnRulesChanged(object? sender, IReadOnlyList<Autofire.Core.Models.MappingRule> focusedRules)
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
