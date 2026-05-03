namespace Autofire.Infrastructure.Configuration;

public static class AppPaths
{
    public static string BaseDirectory => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutofireNext"));

    public static string ProfilesDirectory => Ensure(Path.Combine(BaseDirectory, "profiles"));
    public static string LogsDirectory => Ensure(Path.Combine(BaseDirectory, "logs"));
    public static string SettingsFile => Path.Combine(BaseDirectory, "settings.json");

    public static string GetProfileFile(string profileId)
    {
        return Path.Combine(ProfilesDirectory, $"{profileId}.json");
    }

    private static string Ensure(string path)
    {
        _ = Directory.CreateDirectory(path);
        return path;
    }
}
