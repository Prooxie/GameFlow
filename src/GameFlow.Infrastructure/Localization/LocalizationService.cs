using System.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Localization;

/// <summary>
/// Default <see cref="ILocalizationService"/> that resolves UI strings via
/// <see cref="IStringLocalizer{T}"/> and broadcasts culture changes through
/// <see cref="CultureChanged"/>.
/// </summary>
public sealed class LocalizationService(
    IStringLocalizer<UiText> localizer,
    ILogger<LocalizationService> logger) : ILocalizationService
{
    private readonly IStringLocalizer<UiText> localizer = localizer;
    private readonly ILogger<LocalizationService> logger = logger;
    private CultureInfo selectedCulture = CultureInfo.CurrentUICulture;

    /// <inheritdoc />
    public event EventHandler? CultureChanged;

    /// <summary>
    /// Languages exposed in the UI dropdown. The order here is the order
    /// the user sees, with English pinned first.
    /// </summary>
    /// <remarks>Issue #13: Added all 8 requested languages.</remarks>
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

    /// <inheritdoc />
    public string CurrentCulture => Volatile.Read(ref selectedCulture).TwoLetterISOLanguageName;

    /// <inheritdoc />
    public string this[string key] => Translate(key);

    /// <inheritdoc />
    public string Translate(string key)
    {
        // Listener tasks may have captured the culture before the user changed it.
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = Volatile.Read(ref selectedCulture);
            return localizer[key].Value;
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    /// <inheritdoc />
    public void SetCulture(string cultureCode)
    {
        CultureInfo culture;
        try
        {
            culture = new CultureInfo(cultureCode);
        }
        catch (CultureNotFoundException exception)
        {
            // Don't crash the UI just because someone hand-edited settings.json
            // with a typo — fall back to invariant and tell the operator.
            logger.LogWarning(
                exception,
                "Culture code {CultureCode} is not recognised. Falling back to invariant culture.",
                cultureCode);
            culture = CultureInfo.InvariantCulture;
        }

        var previous = CultureInfo.CurrentUICulture.Name;
        Volatile.Write(ref selectedCulture, culture);

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        if (!string.Equals(previous, culture.Name, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation(
                "UI culture changed: {PreviousCulture} -> {NewCulture}.",
                previous,
                culture.Name);
        }

        CultureChanged?.Invoke(this, EventArgs.Empty);
    }
}
