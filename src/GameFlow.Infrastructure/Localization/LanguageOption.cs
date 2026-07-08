namespace GameFlow.Infrastructure.Localization;

public sealed record LanguageOption(string Code, string DisplayName)
{
    public override string ToString()
    {
        return DisplayName;
    }
}
