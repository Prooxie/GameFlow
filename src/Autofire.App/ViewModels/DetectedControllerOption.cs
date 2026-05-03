namespace Autofire.App.ViewModels;

public sealed record DetectedControllerOption(string Id, string Label, string Description)
{
    public override string ToString()
    {
        return Label;
    }
}
