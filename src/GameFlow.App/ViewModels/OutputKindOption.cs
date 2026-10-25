using GameFlow.Infrastructure.Runtime;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using GameFlow.Infrastructure.Runtime.Templates;

namespace GameFlow.App.ViewModels;

/// <summary>Output-kind option for the template editor's combo box.</summary>
public sealed record OutputKindOption(VirtualControllerKind Kind, string Label)
{
    /// <summary>Builds platform output choices with the current language.</summary>
    public static IReadOnlyList<OutputKindOption> Create(
        GameFlow.Infrastructure.Localization.ILocalizationService localization, bool? isWindows = null)
    {
        var windows = isWindows ?? OperatingSystem.IsWindows();
        return [.. OutputProviderPolicy.GetSelectableKinds(windows).Select(kind => new OutputKindOption(kind,
            windows ? HidMaestroProfiles.LabelFor(kind) : localization["StyleLabelSimpleGamepad"]))];
    }
}
