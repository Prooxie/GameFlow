using Avalonia.Threading;
using GameFlow.Infrastructure.Runtime.Web;

namespace GameFlow.App.ViewModels;

/// <summary>
/// Backs the sheet for one phone: its name, its own link and QR code, and
/// whether it has connected yet.
/// </summary>
/// <remarks>
/// <para>
/// One sheet per phone rather than one shared address: each phone's link
/// names its seat, so scanning it always makes that phone — the one already
/// assigned to a virtual controller — instead of whichever pad number was
/// free. Up to sixteen phones each get their own.
/// </para>
/// <para>
/// Network sharing and the firewall check are server-wide facts, so they
/// stay on the shared <see cref="OverlayPanelViewModel"/> exposed as
/// <see cref="Network"/> rather than being restated here.
/// </para>
/// </remarks>
public sealed class PhoneSheetViewModel : ViewModelBase, IDisposable
{
    private readonly PhoneRegistry registry;
    private readonly WebControllerHub hub;
    private readonly WebControllerServer server;
    private readonly DispatcherTimer pollTimer;
    private string link = string.Empty;
    private bool isConnected;
    private QrCode? code;

    public PhoneSheetViewModel(
        OverlayPanelViewModel network,
        PhoneRegistry registry,
        WebControllerHub hub,
        WebControllerServer server,
        int seat)
    {
        Network = network ?? throw new ArgumentNullException(nameof(network));
        this.registry = registry ?? throw new ArgumentNullException(nameof(registry));
        this.hub = hub ?? throw new ArgumentNullException(nameof(hub));
        this.server = server ?? throw new ArgumentNullException(nameof(server));
        Seat = seat;
        name = registry.NameOf(seat) ?? UiTextResources.Format("UiPhoneNumber", seat + 1);

        // Sharing can be switched on from this very sheet, and the phone
        // connects on its own schedule; both change what the sheet should
        // say, and neither raises anything the sheet could listen to.
        pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        pollTimer.Tick += (_, _) => Refresh();
        Refresh();
        pollTimer.Start();
    }

    /// <summary>Network sharing and firewall state, shared with the Options dialog.</summary>
    public OverlayPanelViewModel Network { get; }

    public int Seat { get; }

    private string name;

    /// <summary>The phone's name. Renaming here renames it everywhere, the phone's own page included.</summary>
    public string Name
    {
        get => name;
        set
        {
            if (SetProperty(ref name, value))
            {
                registry.Rename(Seat, value);
            }
        }
    }

    public string Title => UiTextResources.Format("PhoneConnectTitle", name);

    /// <summary>This phone's own link; empty while sharing is off or the server cannot be reached from the network.</summary>
    public string Link => link;

    public bool HasLink => link.Length > 0;

    public QrCode? Code => code;

    public bool IsConnected => isConnected;

    public string StatusText => !HasLink
        ? UiTextResources.Get("UiNoCodeWhilePhoneSharingIsOff")
        : isConnected
            ? UiTextResources.Format("PhoneConnected", name)
            : UiTextResources.Get("PhoneWaiting");

    public void Refresh()
    {
        var nextLink = server.PhoneLink(Seat);
        if (!string.Equals(nextLink, link, StringComparison.Ordinal))
        {
            link = nextLink;
            code = nextLink.Length == 0 ? null : QrCode.Encode(nextLink, QrErrorCorrection.Medium);
            OnPropertyChanged(nameof(Link));
            OnPropertyChanged(nameof(HasLink));
            OnPropertyChanged(nameof(Code));
        }

        var connected = hub.IsPadConnected(Seat);
        if (connected != isConnected)
        {
            isConnected = connected;
            OnPropertyChanged(nameof(IsConnected));
        }

        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(Title));
    }

    public void Dispose() => pollTimer.Stop();
}
