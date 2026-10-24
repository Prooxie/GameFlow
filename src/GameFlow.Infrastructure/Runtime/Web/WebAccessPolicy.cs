using System.Security.Cryptography;
using System.Text;

namespace GameFlow.Infrastructure.Runtime.Web;

internal static class WebAccessPolicy
{
    internal static bool IsAuthorized(bool loopback, bool allowsRemote, string expected, string? supplied) =>
        loopback || allowsRemote && MatchesToken(expected, supplied);

    /// <summary>
    /// Access rule for the lighting API, deliberately stricter than the
    /// phone pages: the bearer token is required EVEN from this PC, and it
    /// is only ever read from the <c>Authorization</c> header.
    ///
    /// <para>
    /// Loopback alone is not proof of intent for an endpoint that changes
    /// hardware state — any local process or browser tab can reach
    /// localhost. And a token in the query string ends up in logs, browser
    /// history and Referer headers, which is acceptable for a link someone
    /// scans with a phone and not for a credential a script holds forever.
    /// Remote callers additionally need network sharing switched on, the
    /// same consent the phone pages require.
    /// </para>
    /// </summary>
    internal static bool IsApiAuthorized(bool loopback, bool allowsRemote, string expected, string? authorizationHeader)
    {
        if (!loopback && !allowsRemote)
        {
            return false;
        }

        const string Scheme = "Bearer ";
        if (authorizationHeader is null
            || !authorizationHeader.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return MatchesToken(expected, authorizationHeader[Scheme.Length..].Trim());
    }
    internal static bool MatchesToken(string expected, string? supplied) =>
        expected.Length == 64 && supplied?.Length == 64 &&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(supplied));
}
