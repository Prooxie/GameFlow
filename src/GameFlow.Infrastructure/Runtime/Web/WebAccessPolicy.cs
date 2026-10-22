using System.Security.Cryptography;
using System.Text;

namespace GameFlow.Infrastructure.Runtime.Web;

internal static class WebAccessPolicy
{
    internal static bool IsAuthorized(bool loopback, bool allowsRemote, string expected, string? supplied) =>
        loopback || allowsRemote && MatchesToken(expected, supplied);
    internal static bool MatchesToken(string expected, string? supplied) =>
        expected.Length == 64 && supplied?.Length == 64 &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(supplied));
}
