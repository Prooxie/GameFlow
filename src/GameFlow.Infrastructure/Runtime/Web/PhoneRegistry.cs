using System.Text.Json;
using GameFlow.Infrastructure.Configuration;
using GameFlow.Infrastructure.Profiles;
using Microsoft.Extensions.Logging;

namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>One phone the user added, bound to a seat (= pad index) for good.</summary>
/// <param name="Seat">Pad index 0–15. The phone's catalog id is <c>web-pad-{Seat}</c>.</param>
/// <param name="Name">What the app and the phone's own page call it.</param>
public sealed record RegisteredPhone(int Seat, string Name);

/// <summary>
/// The phones the user has added as controllers, persisted to
/// <see cref="AppPaths.PhonesFile"/>.
///
/// <para>
/// Pad numbers used to be handed out to whichever browser connected first,
/// and a phone existed only while it was connected. A slot bound to
/// "Web Controller #1" could therefore get a different phone after a
/// restart, or none, and there was no way to set a phone up before picking
/// it up. A registered phone owns its seat: its link names the seat, it is
/// listed (offline) before it ever connects so it can be assigned to a
/// virtual controller, and it keeps its name.
/// </para>
/// </summary>
public sealed class PhoneRegistry
{
    private readonly WebControllerHub hub;
    private readonly ILogger<PhoneRegistry> logger;
    private readonly string filePath;
    private readonly Lock gate = new();
    private IReadOnlyList<RegisteredPhone> phones = [];
    private IReadOnlySet<int> seats = new HashSet<int>();

    public PhoneRegistry(WebControllerHub hub, ILogger<PhoneRegistry> logger, string? overridePath = null)
    {
        this.hub = hub ?? throw new ArgumentNullException(nameof(hub));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        filePath = overridePath ?? AppPaths.PhonesFile;
        Load();
    }

    /// <summary>Raised after a phone is added, renamed or removed.</summary>
    public event EventHandler? Changed;

    /// <summary>Registered phones, ordered by seat.</summary>
    public IReadOnlyList<RegisteredPhone> Phones
    {
        get { lock (gate) { return phones; } }
    }

    /// <summary>Seats that belong to a registered phone.</summary>
    public IReadOnlySet<int> Seats
    {
        get { lock (gate) { return seats; } }
    }

    public bool CanAdd
    {
        get { lock (gate) { return phones.Count < WebControllerHub.MaxPads; } }
    }

    public bool IsRegistered(int seat) => Seats.Contains(seat);

    public string? NameOf(int seat)
    {
        lock (gate)
        {
            return phones.FirstOrDefault(phone => phone.Seat == seat)?.Name;
        }
    }

    /// <summary>
    /// Adds a phone on the lowest free seat. Returns null when all sixteen
    /// seats are taken.
    /// </summary>
    /// <param name="name">Optional name; "Phone n" when empty.</param>
    public RegisteredPhone? Add(string? name = null)
    {
        RegisteredPhone added;
        lock (gate)
        {
            var seat = Enumerable.Range(0, WebControllerHub.MaxPads)
                .Where(candidate => !seats.Contains(candidate) && !hub.IsPadConnected(candidate))
                .DefaultIfEmpty(-1)
                .First();
            if (seat < 0)
            {
                return null;
            }

            added = new RegisteredPhone(seat, NormalizeName(name, seat));
            PublishLocked([.. phones, added]);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return added;
    }

    /// <summary>
    /// Registers a seat a browser took without a seat of its own — someone
    /// opened the general link. It is a phone from then on, like any other.
    /// </summary>
    public void EnsureRegistered(int seat)
    {
        if (seat is < 0 or >= WebControllerHub.MaxPads)
        {
            return;
        }

        lock (gate)
        {
            if (seats.Contains(seat))
            {
                return;
            }

            PublishLocked([.. phones, new RegisteredPhone(seat, NormalizeName(null, seat))]);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Rename(int seat, string? name)
    {
        lock (gate)
        {
            var existing = phones.FirstOrDefault(phone => phone.Seat == seat);
            var normalized = NormalizeName(name, seat);
            if (existing is null || existing.Name == normalized)
            {
                return;
            }

            PublishLocked([.. phones.Select(phone => phone.Seat == seat ? phone with { Name = normalized } : phone)]);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Removes a phone and disconnects it. Its link stops working: the page
    /// is told it was removed and stops reconnecting.
    /// </summary>
    public void Remove(int seat)
    {
        lock (gate)
        {
            if (!seats.Contains(seat))
            {
                return;
            }

            PublishLocked([.. phones.Where(phone => phone.Seat != seat)]);
        }

        hub.Revoke(seat);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string NormalizeName(string? name, int seat)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return $"Phone {seat + 1}";
        }

        // Shown in the app and sent to the page; keep it a label, not a paragraph.
        return trimmed.Length > 40 ? trimmed[..40] : trimmed;
    }

    /// <summary>Replaces the list, persists it. Caller holds <see cref="gate"/>.</summary>
    private void PublishLocked(IEnumerable<RegisteredPhone> next)
    {
        phones = [.. next.OrderBy(phone => phone.Seat)];
        seats = phones.Select(phone => phone.Seat).ToHashSet();
        try
        {
            AtomicFile.WriteAllText(filePath, JsonSerializer.Serialize(phones, ProfileJsonOptions.Default));
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to save the phone list.");
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            var loaded = JsonSerializer.Deserialize<List<RegisteredPhone>>(File.ReadAllText(filePath), ProfileJsonOptions.Default);
            if (loaded is null)
            {
                return;
            }

            // One phone per seat, seats in range — whatever the file says.
            phones = [.. loaded
                .Where(phone => phone is not null && phone.Seat is >= 0 and < WebControllerHub.MaxPads)
                .GroupBy(phone => phone.Seat)
                .Select(group => group.First() with { Name = NormalizeName(group.First().Name, group.Key) })
                .OrderBy(phone => phone.Seat)];
            seats = phones.Select(phone => phone.Seat).ToHashSet();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Failed to load the phone list; starting with none.");
            phones = [];
            seats = new HashSet<int>();
        }
    }
}
