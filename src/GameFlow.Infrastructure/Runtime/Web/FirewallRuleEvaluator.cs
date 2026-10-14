using System.Globalization;

namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>
/// Decides whether the GameFlow firewall rule lets LAN devices reach the web server.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a program filter fails.</b> <see cref="System.Net.HttpListener"/> uses the
/// HTTP.sys kernel driver. HTTP.sys accepts each connection in the System process,
/// not in GameFlow.App.exe. A rule for GameFlow.App.exe therefore never matches,
/// and Windows drops the connection. Older GameFlow versions made such a rule.
/// </para>
/// <para>
/// <b>Why every active profile must be covered.</b> Windows selects a profile per
/// network adapter. A PC can be on Private and Public at the same time. The phone
/// can use the Public adapter, so the rule must cover each active profile.
/// </para>
/// </remarks>
public static class FirewallRuleEvaluator
{
    /// <summary>IANA protocol number for TCP.</summary>
    public const int ProtocolTcp = 6;

    /// <summary>Firewall COM value for "any protocol".</summary>
    public const int ProtocolAny = 256;

    /// <summary>Program name that the firewall uses for the kernel, where HTTP.sys runs.</summary>
    private const string SystemProgram = "System";

    /// <summary>
    /// Finds the state of the GameFlow rule for <paramref name="port"/>.
    /// </summary>
    /// <param name="rules">All rules from the firewall policy.</param>
    /// <param name="activeProfiles">Bitmask of the profiles that are in force now.</param>
    /// <param name="port">TCP port of the web server.</param>
    /// <returns>
    /// <see cref="FirewallRuleState.Active"/> when one GameFlow rule lets the traffic in.
    /// <see cref="FirewallRuleState.Stale"/> when GameFlow rules exist, but none lets the traffic in.
    /// <see cref="FirewallRuleState.Missing"/> when no enabled inbound GameFlow allow rule exists.
    /// </returns>
    public static FirewallRuleState Evaluate(IEnumerable<FirewallRuleInfo> rules, int activeProfiles, int port)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var state = FirewallRuleState.Missing;
        foreach (var rule in rules)
        {
            if (!IsGameFlowAllowRule(rule))
            {
                continue;
            }

            if (LetsTrafficIn(rule, activeProfiles, port))
            {
                return FirewallRuleState.Active;
            }

            state = FirewallRuleState.Stale;
        }

        return state;
    }

    /// <summary>
    /// Tells if GameFlow must write the rule now. Only an elevated process can write it
    /// without a UAC prompt. The UI button covers a process that is not elevated.
    /// </summary>
    public static bool ShouldAddRule(FirewallRuleState state, bool isElevated) =>
        isElevated && state != FirewallRuleState.Active;

    private static bool IsGameFlowAllowRule(FirewallRuleInfo rule) =>
        string.Equals(rule.Name, WindowsFirewallAccess.RuleName, StringComparison.OrdinalIgnoreCase)
        && rule.IsInbound
        && rule.IsEnabled
        && rule.IsAllow;

    private static bool LetsTrafficIn(FirewallRuleInfo rule, int activeProfiles, int port)
    {
        var isAnyProgram = string.IsNullOrWhiteSpace(rule.ApplicationName)
            || string.Equals(rule.ApplicationName, SystemProgram, StringComparison.OrdinalIgnoreCase);
        var isTcp = rule.Protocol is ProtocolTcp or ProtocolAny;
        var coversProfiles = (activeProfiles & ~rule.Profiles) == 0;

        return isAnyProgram && isTcp && coversProfiles && IncludesPort(rule.LocalPorts, port);
    }

    private static bool IncludesPort(string? localPorts, int port)
    {
        if (string.IsNullOrWhiteSpace(localPorts))
        {
            return false;
        }

        foreach (var token in localPorts.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == "*")
            {
                return true;
            }

            var range = token.Split('-', StringSplitOptions.TrimEntries);
            var hasFirst = int.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first);
            var last = first;
            var hasLast = range.Length == 1
                || (range.Length == 2 && int.TryParse(range[1], NumberStyles.None, CultureInfo.InvariantCulture, out last));

            if (hasFirst && hasLast && port >= first && port <= last)
            {
                return true;
            }
        }

        return false;
    }
}
