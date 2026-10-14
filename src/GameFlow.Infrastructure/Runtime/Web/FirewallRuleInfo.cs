namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>
/// One Windows Firewall rule, with only the fields that decide whether it lets
/// web-controller traffic in. Values come from the firewall COM API (<c>INetFwRule</c>).
/// </summary>
/// <param name="Name">Rule name. GameFlow finds its own rule by <see cref="WindowsFirewallAccess.RuleName"/>.</param>
/// <param name="IsInbound">True for an inbound rule.</param>
/// <param name="IsEnabled">True when the rule is enabled.</param>
/// <param name="IsAllow">True for an allow rule, false for a block rule.</param>
/// <param name="Protocol">IANA protocol number: 6 is TCP, 256 is any protocol.</param>
/// <param name="LocalPorts">"*", or a comma list of ports and ranges, for example "80,8000-8100".</param>
/// <param name="ApplicationName">Program path, or null when the rule applies to all programs.</param>
/// <param name="Profiles">Bitmask of profiles: 0x1 domain, 0x2 private, 0x4 public.</param>
public sealed record FirewallRuleInfo(
    string? Name,
    bool IsInbound,
    bool IsEnabled,
    bool IsAllow,
    int Protocol,
    string? LocalPorts,
    string? ApplicationName,
    int Profiles);
