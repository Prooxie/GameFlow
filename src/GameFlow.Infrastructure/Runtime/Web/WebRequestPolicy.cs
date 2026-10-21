using System.Net;

namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>Limits browser access to the local listener's own origin.</summary>
internal static class WebRequestPolicy
{
    internal static bool Allows(Uri? request, string? origin, IPAddress localAddress)
    {
        if (request is null || request.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        var host = request.DnsSafeHost;
        var localHost = host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(host.Trim('[', ']'), out var address) &&
             (IPAddress.IsLoopback(address) || address.Equals(localAddress)));
        if (!localHost)
        {
            return false;
        }

        // Native clients may omit Origin. Browsers always supply it for WebSockets.
        return origin is null ||
            (Uri.TryCreate(origin, UriKind.Absolute, out var source) &&
             source.Scheme == request.Scheme && source.Port == request.Port &&
             source.DnsSafeHost.Equals(host, StringComparison.OrdinalIgnoreCase) &&
             string.IsNullOrEmpty(source.UserInfo) && source.AbsolutePath == "/" &&
             string.IsNullOrEmpty(source.Query) && string.IsNullOrEmpty(source.Fragment));
    }
}
