namespace GameFlow.Infrastructure.Localization;

public interface ILocalizationService
{
    event EventHandler? CultureChanged;

    IReadOnlyList<LanguageOption> SupportedLanguages { get; }

    string CurrentCulture { get; }

    string this[string key] { get; }

    string Translate(string key);

    void SetCulture(string cultureCode);
}
