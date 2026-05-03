namespace Autofire.App.ViewModels;

public sealed record InputProviderOption(string Key, string Label, string Description)
{
    public override string ToString()
    {
        return Label;
    }
}
