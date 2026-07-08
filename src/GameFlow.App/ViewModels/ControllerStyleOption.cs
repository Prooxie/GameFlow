using GameFlow.Core.Enums;

namespace GameFlow.App.ViewModels;

public sealed record ControllerStyleOption(ControllerVisualStyle Style, string Label)
{
    public override string ToString()
    {
        return Label;
    }
}
