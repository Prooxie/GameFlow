using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Runtime.Motion;
using Microsoft.Extensions.Logging;

namespace GameFlow.App.ViewModels;

/// <summary>
/// Backs the Dashboard's DSU / Cemuhook motion-server section: the enable
/// toggle, the port, and a live status line.
///
/// <para>
/// Its own view-model rather than more surface on
/// <see cref="ShellViewModel"/>, matching
/// <see cref="DashboardControllerPanelViewModel"/>. The shell is already
/// 2500+ lines with a documented three-tier refresh contract, and a
/// self-contained panel has no business being tangled into it.
/// </para>
/// </summary>
public sealed class MotionServerPanelViewModel : ViewModelBase
{
    private readonly IUserSettingsService userSettings;
    private readonly DsuServer server;
    private readonly ILogger<MotionServerPanelViewModel> logger;

    // Guards the settings write while the UI is being populated FROM
    // settings. Without it, assigning the properties below during Refresh
    // would look like a user edit and write straight back — harmless for
    // the value, but it churns the settings file on every tick.
    private bool suppressPersist;
    private long saveRevision;
    private string? saveError;

    public MotionServerPanelViewModel(
        IUserSettingsService userSettings,
        DsuServer server,
        ILogger<MotionServerPanelViewModel> logger)
    {
        this.userSettings = userSettings;
        this.server = server;
        this.logger = logger;

        LoadFromSettings();
    }

    /// <summary>
    /// Whether the motion server should be listening. Writing this
    /// persists immediately; the server picks the change up on its own
    /// (it polls the settings and rebinds), so there is nothing to start
    /// or stop from here.
    /// </summary>
    public bool IsEnabled
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                Persist();
            }
        }
    }

    /// <summary>
    /// UDP port, as text because it is edited in a TextBox. Kept as a
    /// string so validation can explain an invalid edit. The view commits
    /// the text on focus loss, rather than rebinding on every keystroke.
    /// </summary>
    public string Port
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                Persist();
            }
        }
    } = "26760";

    public bool AllowRemote
    {
        get;
        set { if (SetProperty(ref field, value)) { Persist(); } }
    }

    /// <summary>Live one-line status, refreshed by the shell's tick.</summary>
    public string Status
    {
        get;
        private set => SetProperty(ref field, value);
    } = "Stopped";

    /// <summary>True when the port box holds something that is not a usable port.</summary>
    public bool HasPortError
    {
        get;
        private set => SetProperty(ref field, value);
    }

    /// <summary>
    /// Recomputes the status line. Called from the shell's existing
    /// refresh tick rather than owning a timer, so the panel costs
    /// nothing when the Dashboard is not on screen.
    /// </summary>
    public void Refresh()
    {
        if (saveError is not null) { Status = saveError; return; }
        if (HasPortError) { Status = "Port is not valid. The previous saved port is still in use."; return; }
        if (!IsEnabled)
        {
            Status = "Stopped";
            return;
        }

        if (server.LastError is { } error)
        {
            Status = error;
            return;
        }

        if (!server.IsRunning)
        {
            Status = "Starting…";
            return;
        }

        var clients = server.ConnectedClientCount;
        Status = clients switch
        {
            0 => $"Listening on UDP {server.ListenEndpoint} — no emulator connected yet",
            1 => $"Listening on UDP {server.ListenEndpoint} — 1 emulator connected",
            _ => $"Listening on UDP {server.ListenEndpoint} — {clients} emulators connected"
        };
    }

    private void LoadFromSettings()
    {
        var settings = userSettings.Current;
        suppressPersist = true;
        try
        {
            IsEnabled = settings.MotionServerEnabled;
            AllowRemote = settings.MotionServerAllowRemote;
            Port = settings.MotionServerPort.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            suppressPersist = false;
        }
    }

    private void Persist()
    {
        if (suppressPersist)
        {
            return;
        }

        HasPortError = !TryParsePort(Port, out var port);
        // A bad port must never prevent disabling the server or remote access.
        if (HasPortError) { port = userSettings.Current.MotionServerPort; }

        var isEnabled = IsEnabled;
        var allowRemote = AllowRemote;
        var revision = Interlocked.Increment(ref saveRevision);
        _ = SaveAsync();

        async Task SaveAsync()
        {
            try
            {
                await userSettings.UpdateAsync(settings => revision != Volatile.Read(ref saveRevision) ? settings : settings with
                {
                    MotionServerEnabled = isEnabled,
                    MotionServerPort = port,
                    MotionServerAllowRemote = allowRemote
                });
                if (revision == Volatile.Read(ref saveRevision)) { saveError = null; Refresh(); }
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not save the motion server settings.");
                if (revision == Volatile.Read(ref saveRevision))
                {
                    saveError = "Changes were not saved: " + exception.Message;
                    Refresh();
                }
            }
        }
    }

    /// <summary>
    /// Accepts only ports a server can actually bind. Below 1024 is
    /// reserved and needs elevation on most systems, so it is rejected
    /// here rather than surfacing later as a bind failure the user cannot
    /// interpret.
    /// </summary>
    private static bool TryParsePort(string? text, out int port)
    {
        port = 0;
        return int.TryParse(text, System.Globalization.NumberStyles.Integer,
                   System.Globalization.CultureInfo.InvariantCulture, out var parsed)
               && parsed is >= 1024 and <= 65535
               && (port = parsed) == parsed;
    }
}
