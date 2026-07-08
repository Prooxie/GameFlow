namespace GameFlow.App.ViewModels;

public sealed record OutputProviderOption(string Key, string Label, string Description)
{
    public override string ToString()
    {
        return Label;
    }
}
