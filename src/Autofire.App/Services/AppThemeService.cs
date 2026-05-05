using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace Autofire.App.Services;

/// <summary>
/// Applies color-palette themes at runtime by replacing entries in
/// the application's merged resource dictionaries and toggling
/// Avalonia's RequestedThemeVariant for Dark / Light base modes.
/// </summary>
public static class AppThemeService
{
    private static readonly IReadOnlyDictionary<string, string> CyberBlueAccents = new Dictionary<string, string>
    {
        ["AppAccent"]       = "#00D4FF",
        ["AppAccentSoft"]   = "#0F2530",
        ["AppAccentHover"]  = "#22DEFF",
        ["AppBorderActive"] = "#00D4FF",
        ["AppSurface0"]     = "#050B12",
        ["AppSurface1"]     = "#07101D",
        ["AppSurface2"]     = "#0A1628",
        ["AppBorder0"]      = "#0F2035",
        ["AppBorder1"]      = "#1E3A5A",
        ["AppForeground"]   = "#E2E8F0",
        ["AppForegroundDim"]= "#64748B",
        ["AppPrimaryBtn"]   = "#040C16"
    };

    private static readonly IReadOnlyDictionary<string, string> MidnightPurpleAccents = new Dictionary<string, string>
    {
        ["AppAccent"]       = "#A78BFA",
        ["AppAccentSoft"]   = "#1E1035",
        ["AppAccentHover"]  = "#BBA5FF",
        ["AppBorderActive"] = "#A78BFA",
        ["AppSurface0"]     = "#0A0812",
        ["AppSurface1"]     = "#100D1C",
        ["AppSurface2"]     = "#16112A",
        ["AppBorder0"]      = "#221840",
        ["AppBorder1"]      = "#3B2870",
        ["AppForeground"]   = "#EDE9F6",
        ["AppForegroundDim"]= "#6B5F8B",
        ["AppPrimaryBtn"]   = "#0D0820"
    };

    private static readonly IReadOnlyDictionary<string, string> NeonGreenAccents = new Dictionary<string, string>
    {
        ["AppAccent"]       = "#22C55E",
        ["AppAccentSoft"]   = "#0A2818",
        ["AppAccentHover"]  = "#4ADE80",
        ["AppBorderActive"] = "#22C55E",
        ["AppSurface0"]     = "#060C08",
        ["AppSurface1"]     = "#091410",
        ["AppSurface2"]     = "#0C1C14",
        ["AppBorder0"]      = "#112B1A",
        ["AppBorder1"]      = "#1A4B2A",
        ["AppForeground"]   = "#E0F2E9",
        ["AppForegroundDim"]= "#587060",
        ["AppPrimaryBtn"]   = "#040E06"
    };

    private static readonly IReadOnlyDictionary<string, string> SolarRedAccents = new Dictionary<string, string>
    {
        ["AppAccent"]       = "#F97316",
        ["AppAccentSoft"]   = "#2C1206",
        ["AppAccentHover"]  = "#FB923C",
        ["AppBorderActive"] = "#F97316",
        ["AppSurface0"]     = "#0F0802",
        ["AppSurface1"]     = "#1A1005",
        ["AppSurface2"]     = "#221607",
        ["AppBorder0"]      = "#31200A",
        ["AppBorder1"]      = "#5A3A10",
        ["AppForeground"]   = "#FEF3E2",
        ["AppForegroundDim"]= "#826040",
        ["AppPrimaryBtn"]   = "#0E0602"
    };

    private static readonly IReadOnlyDictionary<string, string> LightAccents = new Dictionary<string, string>
    {
        ["AppAccent"]       = "#0077CC",
        ["AppAccentSoft"]   = "#EBF5FF",
        ["AppAccentHover"]  = "#005FAA",
        ["AppBorderActive"] = "#0077CC",
        ["AppSurface0"]     = "#FFFFFF",
        ["AppSurface1"]     = "#F0F4F8",
        ["AppSurface2"]     = "#E2EAF0",
        ["AppBorder0"]      = "#CBD5E0",
        ["AppBorder1"]      = "#94A3B8",
        ["AppForeground"]   = "#0F172A",
        ["AppForegroundDim"]= "#475569",
        ["AppPrimaryBtn"]   = "#FFFFFF"
    };

    public static void Apply(AppThemeKind kind)
    {
        if (Application.Current is null)
        {
            return;
        }

        var palette = kind switch
        {
            AppThemeKind.MidnightPurple => MidnightPurpleAccents,
            AppThemeKind.NeonGreen => NeonGreenAccents,
            AppThemeKind.SolarRed => SolarRedAccents,
            AppThemeKind.Light => LightAccents,
            _ => CyberBlueAccents
        };

        // Toggle Avalonia's RequestedThemeVariant so FluentTheme adapts its base controls
        Application.Current.RequestedThemeVariant = kind == AppThemeKind.Light
            ? ThemeVariant.Light
            : ThemeVariant.Dark;

        // Inject our accent colors into the application resource dictionary
        foreach (var (key, hex) in palette)
        {
            if (Color.TryParse(hex, out var color))
            {
                Application.Current.Resources[key] = new SolidColorBrush(color);
            }
        }
    }
}
