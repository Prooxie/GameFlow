using Autofire.Core.Enums;

namespace Autofire.App.ViewModels;

public sealed record ControllerStyleOption(ControllerVisualStyle Style, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}
