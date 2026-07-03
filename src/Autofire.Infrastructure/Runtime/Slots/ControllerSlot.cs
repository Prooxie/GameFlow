using Autofire.Infrastructure.Runtime.Templates;

namespace Autofire.Infrastructure.Runtime.Slots;

/// <summary>
/// A single controller slot: a self-contained unit that maps one or more
/// assigned physical input devices through its own mapping pipeline into
/// one virtual controller described by <see cref="OutputTemplate"/>.
///
/// <para>This is the Phase 3 analogue of PadForge's per-pad slot config
/// (SlotCreated / SlotEnabled / SlotControllerTypes + assigned devices),
/// adapted to Autofire: the per-slot output configuration is the
/// <see cref="DeviceOutputTemplate"/> introduced in Phase 2a, now owned
/// by the slot rather than keyed per physical device.</para>
///
/// <para>Mutable POCO so it round-trips through System.Text.Json and the
/// management UI (Phase 3c) can bind to it.</para>
/// </summary>
public sealed class ControllerSlot
{
    /// <summary>Stable identifier, assigned once at creation.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Display position / ordering (0-based). Lower sorts first.</summary>
    public int Index { get; set; }

    /// <summary>Human-readable name (defaults from the output kind).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When false, the runtime skips this slot entirely.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Catalog ids of the physical input devices feeding this slot. A slot
    /// may aggregate several devices (e.g. a stick + a throttle), matching
    /// PadForge's multi-device-per-slot model.
    /// </summary>
    public List<string> InputDeviceIds { get; set; } = [];

    /// <summary>
    /// The virtual controller this slot emits — output kind, lighting,
    /// rumble, FFB, adaptive triggers, generic shape. The slot owns its
    /// template; <see cref="OutputTemplate"/>.DeviceId carries the slot id.
    /// </summary>
    public DeviceOutputTemplate OutputTemplate { get; set; } = new();

    /// <summary>
    /// Ordered ids of the mapping profiles layered onto this slot. Empty
    /// runs a neutral empty profile (no remapping). Later entries are
    /// applied after earlier ones (their rules win on overlap).
    /// </summary>
    public List<string> ProfileIds { get; set; } = [];

    /// <summary>
    /// Deprecated single-profile field, kept so older slot files still
    /// deserialize; migrated into <see cref="ProfileIds"/> on load.
    /// </summary>
    public string? ProfileId { get; set; }

    /// <summary>Deep-ish copy for detached editing (template cloned too).</summary>
    public ControllerSlot Clone() => new()
    {
        Id = Id,
        Index = Index,
        Name = Name,
        Enabled = Enabled,
        InputDeviceIds = [.. InputDeviceIds],
        OutputTemplate = OutputTemplate.Clone(),
        ProfileIds = [.. ProfileIds],
        ProfileId = ProfileId,
    };
}
