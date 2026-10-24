using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Threading;
using GameFlow.Infrastructure.Localization;

namespace GameFlow.App.Services;

/// <summary>Shared translated text for XAML resources and UI-only helpers.</summary>
public static class UiTextResources
{
    private static readonly Lazy<Dictionary<string, string>> English = new(ReadEnglish);
    private static ILocalizationService? localization;
    private static readonly List<WeakReference<ViewModels.ViewModelBase>> ViewModels = [];
    private static readonly Lock Gate = new();

    /// <summary>Tracks a live view model without retaining it.</summary>
    internal static void Track(ViewModels.ViewModelBase viewModel)
    {
        lock (Gate)
        {
            ViewModels.RemoveAll(reference => !reference.TryGetTarget(out _));
            ViewModels.Add(new(viewModel));
        }
    }

    /// <summary>Gets a translated entry with the English entry as its fallback.</summary>
    public static string Get(string key)
    {
        if (!English.Value.TryGetValue(key, out var fallback))
        {
            throw new InvalidOperationException($"Missing UI translation key: {key}");
        }
        return localization is null ? fallback : localization[key];
    }

    /// <summary>Attaches the application translation service.</summary>
    public static void Attach(ILocalizationService service)
    {
        if (localization is not null) { localization.CultureChanged -= OnCultureChanged; }
        localization = service;
        localization.CultureChanged += OnCultureChanged;
        Refresh();
    }

    /// <summary>Releases the application translation service.</summary>
    public static void Detach()
    {
        if (localization is not null) { localization.CultureChanged -= OnCultureChanged; }
        localization = null;
        lock (Gate) { ViewModels.RemoveAll(reference => !reference.TryGetTarget(out _)); }
    }

    /// <summary>Refreshes resources and live view models on the UI thread.</summary>
    public static void Refresh()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Refresh); return; }
        if (Application.Current is not { } application) { return; }
        foreach (var key in English.Value.Keys)
        {
            application.Resources["Ui." + key] = Get(key);
        }
        ViewModels.ViewModelBase[] live;
        lock (Gate)
        {
            live = ViewModels.Select(reference => reference.TryGetTarget(out var model) ? model : null)
                .OfType<ViewModels.ViewModelBase>().ToArray();
        }
        foreach (var model in live) { model.RefreshResourceText(); }
    }

    /// <summary>Formats a translated entry with values in the current culture.</summary>
    public static string Format(string key, params object?[] arguments) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, Get(key), arguments);

    /// <summary>Gets the translated label for an enum value.</summary>
    public static string EnumValue(Enum option) => Get(option.GetType().Name + "." + option);

    private static void OnCultureChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) { Refresh(); }
        else { Dispatcher.UIThread.Post(Refresh); }
    }

    private static Dictionary<string, string> ReadEnglish()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Localization", "en.po");
        var text = File.ReadAllText(path);
        var entries = Regex.Matches(text,
            @"^msgid ""((?:[^""\\]|\\.)+)""\s*\r?\nmsgstr ""((?:[^""\\]|\\.)*)""", RegexOptions.Multiline);
        return entries.ToDictionary(
            match => Regex.Unescape(match.Groups[1].Value),
            match => Regex.Unescape(match.Groups[2].Value), StringComparer.Ordinal);
    }
}
