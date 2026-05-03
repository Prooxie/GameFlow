using Autofire.Core.Enums;

namespace Autofire.Core.Models;

public sealed record UiPreferences
{
    public string LanguageCode { get; init; } = "en";
    public bool ShowPreviewPane { get; init; } = true;
    public string Theme { get; init; } = "System";
    public bool StartMinimized { get; init; }
    public ControllerVisualStyle PhysicalControllerStyle { get; init; } = ControllerVisualStyle.Auto;
    public ControllerVisualStyle VirtualControllerStyle { get; init; } = ControllerVisualStyle.PlayStation5;
    public bool ShowRawMonitor { get; init; } = true;
}
