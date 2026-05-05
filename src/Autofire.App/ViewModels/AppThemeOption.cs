namespace Autofire.App.ViewModels;

public enum AppThemeKind
{
    CyberBlue,
    MidnightPurple,
    NeonGreen,
    SolarRed,
    Light
}

public sealed record AppThemeOption(AppThemeKind Kind, string Label)
{
    public override string ToString() => Label;
}
