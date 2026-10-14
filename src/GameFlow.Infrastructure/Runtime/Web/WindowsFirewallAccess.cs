using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>
/// Whether the inbound firewall rule for the web controller exists and
/// lets LAN devices in.
/// </summary>
public enum FirewallRuleState
{
    /// <summary>Could not be determined — not Windows, or the query failed.</summary>
    Unknown = 0,

    /// <summary>No enabled inbound allow rule for GameFlow.</summary>
    Missing,

    /// <summary>
    /// A GameFlow rule exists, but it does not let the traffic in. It has a
    /// program filter, another port, or it does not cover each active
    /// network profile. See <see cref="FirewallRuleEvaluator"/>.
    /// </summary>
    Stale,

    /// <summary>A GameFlow rule exists and lets LAN devices in.</summary>
    Active,
}

/// <summary>
/// Checks for, and creates, the inbound Windows Firewall rule
/// the web controller needs before a phone or OBS PC can reach it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Binding the listener and being reachable are
/// two different things, and the failure looks identical to a bug. The
/// server can start perfectly, log
/// <c>running on http://192.168.x.x:8080</c>, and still leave the phone
/// sitting on a connection timeout, because Windows Firewall blocks
/// unsolicited inbound TCP by default.
/// </para>
/// <para>
/// <b>Existing is not the same as applying.</b> <see cref="GetStatusAsync"/>
/// checks the rule fields, not only its name. Earlier versions made a rule for
/// GameFlow.App.exe. That rule never matched, because HTTP.sys accepts the
/// connections in the System process. <see cref="FirewallRuleEvaluator"/>
/// holds these rules.
/// </para>
/// <para>
/// <b>Detection goes through the firewall COM API, not netsh output.</b>
/// It returns the profile bitmasks as numbers, where netsh returns
/// localised prose — and this app ships in eight languages. Creating the
/// rule shells out to netsh, because that path needs elevation and
/// a UAC prompt is easier to raise around a process than around a COM
/// call.
/// </para>
/// <para>
/// <b>Automatic when elevated.</b> <see cref="EnsureInboundRuleAsync"/> runs
/// when the web server starts. An elevated GameFlow writes the rule with no
/// prompt. A GameFlow that is not elevated only logs the state, and the
/// "Allow through firewall" button raises the UAC prompt.
/// </para>
/// <para>
/// <b>Every network, LAN addresses only.</b> The rule covers the Domain,
/// Private and Public profiles, because home Ethernet is often filed as
/// Public. It accepts only local-subnet and private addresses, so the
/// internet cannot reach the server (see <see cref="FirewallCommandBuilder"/>).
/// </para>
/// </remarks>
public sealed class WindowsFirewallAccess(ILogger<WindowsFirewallAccess> logger)
{
    private readonly ILogger<WindowsFirewallAccess> logger = logger;

    /// <summary>
    /// Name the rule is created under, and looked up by. Stable: it is
    /// the identity of the rule across app restarts and upgrades.
    /// </summary>
    public const string RuleName = "GameFlow web controller";

    /// <summary>Inbound direction, per NET_FW_RULE_DIRECTION_.</summary>
    private const int DirectionInbound = 1;

    /// <summary>Allow action, per NET_FW_ACTION_.</summary>
    private const int ActionAllow = 1;

    /// <summary>Win32 error code when the user declines the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;

    /// <summary>False on platforms where this whole concern does not apply.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Reads whether an inbound rule lets LAN devices reach <paramref name="port"/>.
    /// </summary>
    /// <param name="port">TCP port of the web server.</param>
    /// <param name="cancellationToken">Cancels the wait for the COM query.</param>
    public Task<FirewallRuleState> GetStatusAsync(int port, CancellationToken cancellationToken = default)
    {
        if (!IsSupported)
        {
            return Task.FromResult(FirewallRuleState.Unknown);
        }

        // COM calls block; keep them off the caller's thread since this
        // runs while a dialog is opening.
        return Task.Run(() => ReadStatus(port), cancellationToken);
    }

    private FirewallRuleState ReadStatus(int port)
    {
        // OperatingSystem.IsWindows() rather than the IsSupported property:
        // both say the same thing, but only the direct call is a guard the
        // platform-compatibility analyzer can follow into the COM code below.
        if (!OperatingSystem.IsWindows())
        {
            return FirewallRuleState.Unknown;
        }

        try
        {
            var policyType = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (policyType is null || Activator.CreateInstance(policyType) is not { } policyInstance)
            {
                return FirewallRuleState.Unknown;
            }

            dynamic policy = policyInstance;

            // Bitmask of the categories currently in force. More than one
            // can apply at once on a machine with several adapters up —
            // this one has Ethernet, Tailscale and ZeroTier, for instance.
            int activeProfiles = policy.CurrentProfileTypes;

            var rules = new List<FirewallRuleInfo>();
            foreach (dynamic rule in policy.Rules)
            {
                string? name = rule.Name;
                if (!string.Equals(name, RuleName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                rules.Add(ReadRule(name, rule));
            }

            var state = FirewallRuleEvaluator.Evaluate(rules, activeProfiles, port);
            logger.LogDebug(
                "Web controller firewall: state={State} activeProfiles=0x{Active:X} rules={Count}.",
                state, activeProfiles, rules.Count);
            return state;
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Web controller: could not query the Windows Firewall.");
            return FirewallRuleState.Unknown;
        }
    }

    /// <summary>Copies the fields that <see cref="FirewallRuleEvaluator"/> needs out of one COM rule.</summary>
    private static FirewallRuleInfo ReadRule(string? name, dynamic rule)
    {
        int direction = rule.Direction;
        bool isEnabled = rule.Enabled;
        int action = rule.Action;
        int protocol = rule.Protocol;
        string? localPorts = rule.LocalPorts;
        string? applicationName = rule.ApplicationName;
        int profiles = rule.Profiles;

        return new FirewallRuleInfo(
            name,
            direction == DirectionInbound,
            isEnabled,
            action == ActionAllow,
            protocol,
            localPorts,
            applicationName,
            profiles);
    }

    /// <summary>
    /// Writes the inbound rule and the URL reservation when GameFlow runs elevated
    /// and the rule does not let LAN devices in. The web server calls this once at start.
    /// </summary>
    /// <param name="port">TCP port of the web server.</param>
    /// <param name="cancellationToken">Cancels the check and the wait for netsh.</param>
    public async Task EnsureInboundRuleAsync(int port, CancellationToken cancellationToken)
    {
        if (!IsSupported)
        {
            return;
        }

        var state = await GetStatusAsync(port, cancellationToken).ConfigureAwait(false);
        if (FirewallRuleEvaluator.ShouldAddRule(state, Environment.IsPrivilegedProcess))
        {
            _ = await TryAddInboundRuleAsync(port, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (state != FirewallRuleState.Active)
        {
            logger.LogInformation(
                "Web controller: the firewall rule is {State} and GameFlow is not elevated. " +
                "Click 'Allow through firewall' in Options, or start GameFlow as administrator once.",
                state);
        }
    }

    /// <summary>
    /// Writes the inbound rule and the URL reservation. Raises a UAC prompt
    /// when GameFlow is not elevated.
    /// </summary>
    /// <param name="port">TCP port to open.</param>
    /// <param name="cancellationToken">Cancels the wait, not the prompt.</param>
    /// <returns>
    /// <see langword="true"/> when the rule was created. Declining the UAC
    /// prompt returns <see langword="false"/> and is not an error — it is
    /// an answer.
    /// </returns>
    public async Task<bool> TryAddInboundRuleAsync(int port, CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var command = FirewallCommandBuilder.BuildAllowCommand(port, GetCurrentUserSid());
        try
        {
            var exitCode = await RunCommandAsync(command, Environment.IsPrivilegedProcess, cancellationToken)
                .ConfigureAwait(false);

            if (exitCode == 0)
            {
                logger.LogInformation(
                    "Web controller: added inbound firewall rule {Rule} for TCP {Port}, LAN addresses only.",
                    RuleName, port);
                return true;
            }

            logger.LogWarning(
                "Web controller: adding the firewall rule exited with {ExitCode}; the port is probably still blocked.",
                exitCode);
            return false;
        }
        catch (System.ComponentModel.Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            logger.LogInformation("Web controller: the firewall prompt was declined.");
            return false;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            logger.LogWarning(exception, "Web controller: adding the firewall rule failed.");
            return false;
        }
    }

    /// <summary>
    /// SID of the user who runs GameFlow. An elevated process keeps the SID of
    /// the signed-in user, so the reservation also works when GameFlow is not elevated.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? GetCurrentUserSid()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return identity.User?.Value;
    }

    /// <summary>
    /// Runs <paramref name="command"/> in <c>cmd.exe /c</c> and returns its exit code.
    /// </summary>
    /// <param name="command">The command line from <see cref="FirewallCommandBuilder"/>.</param>
    /// <param name="isElevated">
    /// True when this process is elevated. The child then gets the same token
    /// and Windows shows no prompt. Otherwise the shell verb <c>runas</c> raises
    /// the UAC prompt.
    /// </param>
    /// <param name="cancellationToken">Cancels the wait, not the process.</param>
    private static async Task<int> RunCommandAsync(string command, bool isElevated, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("cmd.exe", $"/c {command}")
        {
            CreateNoWindow = true,
            // Elevation goes through the shell verb, which needs the shell.
            UseShellExecute = !isElevated,
            WindowStyle = ProcessWindowStyle.Hidden,
            Verb = isElevated ? string.Empty : "runas",
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start cmd.exe.");

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return process.ExitCode;
    }
}
