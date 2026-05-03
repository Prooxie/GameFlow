using System.Globalization;
using Microsoft.Extensions.Localization;

namespace Autofire.Infrastructure.Localization;

public sealed class LocalizationService(IStringLocalizer<UiText> localizer) : ILocalizationService
{
    private readonly IStringLocalizer<UiText> localizer = localizer;

    public event EventHandler? CultureChanged;

    // Issue #13: Added all 8 requested languages.
    public IReadOnlyList<LanguageOption> SupportedLanguages { get; } =
        [
            new LanguageOption("en", "English"),
            new LanguageOption("cs", "Čeština"),
            new LanguageOption("de", "Deutsch"),
            new LanguageOption("es", "Español"),
            new LanguageOption("fr", "Français"),
            new LanguageOption("it", "Italiano"),
            new LanguageOption("pl", "Polski"),
            new LanguageOption("ru", "Русский"),
        ];

    public string CurrentCulture => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;

    public string this[string key] => Translate(key);

    public string Translate(string key)
    {
        return localizer[key].Value;
    }

    public void SetCulture(string cultureCode)
    {
        var culture = new CultureInfo(cultureCode);

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
