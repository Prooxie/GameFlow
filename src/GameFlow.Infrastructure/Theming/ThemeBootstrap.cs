using System.Security.Cryptography;
using System.Text;
using GameFlow.Infrastructure.Configuration;
using Serilog;

namespace GameFlow.Infrastructure.Theming;

/// <summary>Installs bundled artwork through staging, retaining earlier copies as backups.</summary>
public static class ThemeBootstrap
{
    public const string BundledThemesFolderName = "themes";
    private static readonly Lock Gate = new();
    private static readonly EnumerationOptions Files = new()
    {
        RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint
    };

    public static int EnsureBundledThemesInstalled() => EnsureBundledThemesInstalled(
        Path.Combine(AppContext.BaseDirectory, BundledThemesFolderName), AppPaths.ThemesDirectory);

    internal static int EnsureBundledThemesInstalled(string bundleRoot, string userRoot)
    {
        lock (Gate)
        {
            if (!Directory.Exists(bundleRoot)) { return 0; }
            Directory.CreateDirectory(userRoot);
            var parent = Directory.GetParent(Path.GetFullPath(userRoot))!.FullName;
            var backups = Path.Combine(parent, "theme-backups");
            var staging = Path.Combine(parent, "theme-staging");
            var installed = 0;
            foreach (var source in Directory.EnumerateDirectories(bundleRoot))
            {
                if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) { continue; }
                var name = Path.GetFileName(source);
                var destination = Child(userRoot, name);
                if (Directory.Exists(destination) && (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                {
                    Log.Warning("Bundled theme {Name} was not installed over a symbolic link.", name);
                    continue;
                }
                string? backup = null;
                try
                {
                    var stamp = ComputeBundleStamp(source);
                    var stampPath = Path.Combine(destination, ".bundled.stamp");
                    if (File.Exists(stampPath) && File.ReadAllText(stampPath).Trim() == stamp) { continue; }
                    var stage = Child(staging, name + "-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(stage);
                    foreach (var file in Directory.EnumerateFiles(source, "*", Files))
                    {
                        var relative = Path.GetRelativePath(source, file);
                        if (Path.GetFileName(file) is ".gitkeep" or ".bundled.stamp") { continue; }
                        var target = Child(stage, relative);
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(file, target);
                    }
                    File.WriteAllText(Path.Combine(stage, ".bundled.stamp"), stamp);
                    if (Directory.Exists(destination))
                    {
                        Directory.CreateDirectory(backups);
                        backup = Child(backups, name + "-" + Guid.NewGuid().ToString("N"));
                        Directory.Move(destination, backup);
                    }
                    try { Directory.Move(stage, destination); }
                    catch
                    {
                        if (backup is not null && !Directory.Exists(destination)) { Directory.Move(backup, destination); }
                        throw;
                    }
                    installed++;
                }
                catch (Exception exception)
                {
                    Log.Warning(exception, "Could not install bundled theme {Name}. Earlier artwork was retained.", name);
                }
            }
            return installed;
        }
    }

    private static string Child(string root, string relative)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { throw new IOException("Theme path leaves its destination directory."); }
        return path;
    }

    private static string ComputeBundleStamp(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in Directory.EnumerateFiles(directory, "*", Files).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(file) is ".gitkeep" or ".bundled.stamp") { continue; }
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, file).Replace('\\', '/') + "\0"));
            using var stream = File.OpenRead(file);
            hash.AppendData(SHA256.HashData(stream));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}
