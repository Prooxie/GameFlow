namespace GameFlow.Infrastructure.Updates;

internal static class UpdateDownloadPolicy
{
    internal static bool IsHttps(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);

    internal static bool IsSafeFileName(string name) => !string.IsNullOrWhiteSpace(name) &&
        name is not ("." or "..") && name.IndexOfAny(['/', '\\', ':', '<', '>', '"', '|', '?', '*']) < 0 &&
        !name.Any(char.IsControl) && !name.EndsWith('.') && !name.EndsWith(' ');

    internal static bool IsArchive(string name) => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
}
