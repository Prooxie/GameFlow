using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using GameFlow.Infrastructure.Runtime.Web;

namespace GameFlow.App.ViewModels;

/// <summary>
/// The web server's network-facing state: phone sharing on or off, the
/// private link's reset, and whether Windows Firewall lets phones in.
/// Shared by the Options dialog and every phone's sheet.
///
/// <para>
/// Its own view-model rather than more surface on
/// <see cref="ShellViewModel"/>, for the same reason
/// <see cref="MotionServerPanelViewModel"/> is — see that type.
/// </para>
///
/// <para>
/// It used to build the OBS overlay URL and the single phone address too.
/// Both moved to where they are used: each dashboard panel copies its own
/// OBS link (see <c>OverlayUrl</c>), and each phone has its own link and QR
/// code (see <see cref="PhoneSheetViewModel"/>).
/// </para>
/// </summary>
public sealed class OverlayPanelViewModel : ViewModelBase
{
    private readonly WebControllerServer server;
    private readonly WindowsFirewallAccess firewallAccess;

    public OverlayPanelViewModel(WebControllerServer server, WindowsFirewallAccess firewall)
    {
        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.firewallAccess = firewall ?? throw new ArgumentNullException(nameof(firewall));

        AllowThroughFirewallCommand = new AsyncRelayCommand(AllowThroughFirewallAsync);
        ToggleNetworkSharingCommand = new AsyncRelayCommand(ToggleNetworkSharingAsync);
        ResetPrivateLinkCommand = new AsyncRelayCommand(ResetPrivateLinkAsync);

        Refresh();
    }

    // ─── Reachability ─────────────────────────────────────────────────
    //
    // Binding the port and being reachable are different things, and the
    // gap between them is the single most confusing failure this feature
    // has: the address in the box is correct, the log says the server is
    // running, and the phone still times out — because Windows Firewall
    // drops the inbound connection and says so to nobody.
    //
    // The subtler half is that a rule can exist and still not apply. Older
    // GameFlow versions made a rule for GameFlow.App.exe, which never
    // matches because HTTP.sys accepts the connection in the System
    // process. That rule is "stale", and one click replaces it.
    //
    // An elevated GameFlow writes the rule by itself when the server
    // starts. The button is for a GameFlow that is not elevated.

    private FirewallRuleState firewall = FirewallRuleState.Unknown;
    private bool firewallBusy;
    private bool restartNeeded;

    /// <summary>
    /// Writes the inbound firewall rule and the URL reservation; raises a UAC
    /// prompt when GameFlow is not elevated.
    /// </summary>
    public ICommand AllowThroughFirewallCommand { get; }
    public ICommand ToggleNetworkSharingCommand { get; }
    public ICommand ResetPrivateLinkCommand { get; }
    public bool NetworkSharingEnabled => server.AllowsRemote;
    public string NetworkSharingButtonLabel => server.AllowsRemote ? UiTextResources.Get("UiStopSharing") : UiTextResources.Get("UiEnableSharing");
    public string NetworkSharingStatus { get; private set; } = string.Empty;

    private async Task ResetPrivateLinkAsync()
    {
        try
        {
            await server.ResetAccessTokenAsync();
            NetworkSharingStatus = UiTextResources.Get("UiLinksReset");
        }
        catch (Exception exception) { NetworkSharingStatus = UiTextResources.Format("UiLinkResetFailed", exception.Message); }
        OnPropertyChanged(nameof(NetworkSharingStatus));
        Refresh();
    }

    private async Task ToggleNetworkSharingAsync()
    {
        try
        {
            await server.SetRemoteAccessAsync(!server.AllowsRemote);
            NetworkSharingStatus = server.AllowsRemote
                ? UiTextResources.Get("UiSharingOn")
                : UiTextResources.Get("UiSharingOff");
        }
        catch (Exception exception) { NetworkSharingStatus = UiTextResources.Format("UiSharingFailed", exception.Message); }
        OnPropertyChanged(nameof(NetworkSharingStatus));
        OnPropertyChanged(nameof(NetworkSharingEnabled));
        OnPropertyChanged(nameof(NetworkSharingButtonLabel));
        Refresh();
    }

    /// <summary>
    /// Plain-language state of "can a phone actually reach this?".
    /// </summary>
    public string ReachabilityMessage
    {
        get
        {
            if (!server.AllowsRemote) { return UiTextResources.Get("UiSharingDisabledHint"); }
            if (!server.IsRunning)
            {
                return UiTextResources.Get("UiServerStopped");
            }

            if (IsLocalOnly)
            {
                return restartNeeded
                    ? UiTextResources.Get("UiRestartNetwork")
                    : UiTextResources.Get("UiLocalOnlyHint");
            }

            return firewall switch
            {
                FirewallRuleState.Active =>
                    UiTextResources.Get("UiFirewallAllowed"),

                FirewallRuleState.Stale =>
                    UiTextResources.Get("UiFirewallStale"),

                FirewallRuleState.Missing =>
                    UiTextResources.Get("UiFirewallMissing"),

                _ => UiTextResources.Get("UiFirewallCheckHint"),
            };
        }
    }

    /// <summary>True while the reachability state is a known problem, so the view can call it out.</summary>
    public bool HasReachabilityWarning =>
        server.AllowsRemote && server.IsRunning &&
        (firewall is FirewallRuleState.Missing or FirewallRuleState.Stale || IsLocalOnly);

    /// <summary>
    /// Gates the "Allow through firewall" button: only offered
    /// where it can help, and never while a prompt is already open.
    /// </summary>
    public bool CanAllowThroughFirewall =>
        WindowsFirewallAccess.IsSupported && server.AllowsRemote && server.IsRunning && !firewallBusy && !restartNeeded &&
        (firewall != FirewallRuleState.Active || IsLocalOnly);

    /// <summary>True when the server could bind only to localhost, so no other device can reach it.</summary>
    private bool IsLocalOnly =>
        server.ListenUrl?.Contains("localhost", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Re-reads the firewall state.
    /// </summary>
    /// <remarks>
    /// Runs off the constructor path deliberately — it walks the firewall
    /// rule collection over COM, and blocking the Settings dialog opening
    /// on that would be a poor trade for a line of status text.
    /// </remarks>
    public async Task RefreshReachabilityAsync()
    {
        firewall = await firewallAccess.GetStatusAsync(server.Port).ConfigureAwait(true);
        RaiseReachabilityChanged();
    }

    private async Task AllowThroughFirewallAsync()
    {
        firewallBusy = true;
        RaiseReachabilityChanged();
        try
        {
            var added = await firewallAccess.TryAddInboundRuleAsync(server.Port).ConfigureAwait(true);

            // The listener binds when the server starts, so the new URL
            // reservation takes effect only after a restart.
            restartNeeded = added && IsLocalOnly;

            // Re-read rather than assume: the user may have declined the
            // UAC prompt, and the rule's real fields are what matters.
            firewall = await firewallAccess.GetStatusAsync(server.Port).ConfigureAwait(true);
        }
        finally
        {
            firewallBusy = false;
            RaiseReachabilityChanged();
        }
    }

    private void RaiseReachabilityChanged()
    {
        OnPropertyChanged(nameof(ReachabilityMessage));
        OnPropertyChanged(nameof(HasReachabilityWarning));
        OnPropertyChanged(nameof(CanAllowThroughFirewall));
    }

    /// <summary>
    /// Re-reads the server state the bindings show. Called whenever a view
    /// using this is shown: sharing can be switched from another window.
    /// </summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(NetworkSharingEnabled));
        OnPropertyChanged(nameof(NetworkSharingButtonLabel));
        RaiseReachabilityChanged();
    }
}

/// <summary>One entry in a picker that names something by id.</summary>
/// <param name="Id">The id, or empty for a "let the app decide" entry.</param>
/// <param name="Label">What the combo box shows.</param>
public sealed record OverlayChoice(string Id, string Label);
