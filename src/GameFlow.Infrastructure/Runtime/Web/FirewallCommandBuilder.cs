namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>
/// Builds the <c>cmd.exe /c</c> command line that opens the web server to the LAN.
/// </summary>
/// <remarks>
/// <para>The command does three steps, in this order:</para>
/// <list type="number">
/// <item>It reserves <c>http://+:port/</c> for the current user. A process that is not
/// elevated can then listen on all interfaces. An existing reservation makes this step
/// fail, and the command ignores that failure.</item>
/// <item>It deletes the old GameFlow rule. A second run then replaces the rule.</item>
/// <item>It adds the inbound rule. The exit code of the command is the exit code of this step.</item>
/// </list>
/// <para>
/// The rule has no program filter, because HTTP.sys accepts the connections in the System
/// process (see <see cref="FirewallRuleEvaluator"/>). The rule covers all profiles, but it
/// accepts only local and private addresses. Internet addresses stay blocked.
/// </para>
/// </remarks>
public static class FirewallCommandBuilder
{
    /// <summary>
    /// Remote addresses that the rule accepts: the local subnet, the IPv4 private ranges
    /// (RFC 1918), IPv4 link-local, IPv6 unique-local, and IPv6 link-local.
    /// </summary>
    public const string LanRemoteAddresses =
        "localsubnet,10.0.0.0/8,172.16.0.0/12,192.168.0.0/16,169.254.0.0/16,fc00::/7,fe80::/10";

    private const int MinimumPort = 1;
    private const int MaximumPort = 65535;

    private const string RuleDescription =
        "Lets phones and OBS on your local network reach the GameFlow web controller.";

    /// <summary>
    /// Builds the command line for <c>cmd.exe /c</c>.
    /// </summary>
    /// <param name="port">TCP port of the web server, 1 to 65535.</param>
    /// <param name="userSid">
    /// SID of the user who runs GameFlow, for the URL reservation. Null skips the reservation.
    /// A SID is used because the name "Everyone" is different in each Windows language.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">The port is outside 1 to 65535.</exception>
    public static string BuildAllowCommand(int port, string? userSid)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(port, MinimumPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, MaximumPort);

        var reserveUrl = string.IsNullOrWhiteSpace(userSid)
            ? string.Empty
            : $"netsh http add urlacl url=http://+:{port}/ sddl=\"D:(A;;GX;;;{userSid})\" >nul 2>&1 & ";

        return reserveUrl +
            $"netsh advfirewall firewall delete rule name=\"{WindowsFirewallAccess.RuleName}\" dir=in >nul 2>&1 & " +
            $"netsh advfirewall firewall add rule name=\"{WindowsFirewallAccess.RuleName}\" " +
            $"dir=in action=allow protocol=TCP localport={port} " +
            $"remoteip={LanRemoteAddresses} profile=any " +
            $"description=\"{RuleDescription}\"";
    }
}
