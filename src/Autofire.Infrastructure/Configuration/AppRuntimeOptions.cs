namespace Autofire.Infrastructure.Configuration;

public sealed class AppRuntimeOptions
{
    public int DashboardRefreshHz { get; set; } = 30;
    public bool StartRuntimeOnLaunch { get; set; } = true;
    public string DefaultCulture { get; set; } = "en";
    public bool EnableExperimentalGameInput { get; set; } = false;

    /// <summary>
    /// Enables the ViGEm Bus virtual controller output providers (vigem-xbox360, vigem-ds4).
    /// Requires the ViGEm Bus driver to be installed: https://github.com/nefarius/ViGEmBus/releases
    /// </summary>
    public bool EnableViGEm { get; set; } = true;
}
