namespace GameFlow.Infrastructure.Runtime.Templates;

/// <summary>The virtual controller a device's output template emulates.</summary>
public enum VirtualControllerKind
{
    Xbox360 = 0,
    DualShock4 = 1,
    DualSense = 2,
    GenericDirectInput = 3,

    // Additive members (new builds only ever ADD here — values are
    // pinned and JSON persists the enum as a string, so files written
    // by older builds keep loading and vice versa).
    XboxOne = 4,
    XboxSeries = 5,
    SwitchPro = 6,
    SteamController = 7,
}

/// <summary>
/// A per-device output template — what virtual controller a physical
/// device should present through HidMaestro, plus its lighting, rumble,
/// force-feedback and adaptive-trigger configuration. The field set is
/// grounded in the established slot-config shape (ExtendedSlotConfig /
/// PlayStationSlotConfig) so it maps cleanly onto HidMaestro's
/// <c>HMProfileBuilder</c> / <c>HidDescriptorBuilder</c> once the output
/// sink is wired (Phase 2b).
///
/// <para>Mutable POCO so it round-trips through System.Text.Json and the
/// editor view-model can bind to it directly.</para>
/// </summary>
public sealed class DeviceOutputTemplate
{
    /// <summary>Catalog id of the physical device this template applies to.</summary>
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>When false, the device drives no virtual output (template inert).</summary>
    public bool Enabled { get; set; }

    /// <summary>Which virtual controller to present.</summary>
    public VirtualControllerKind OutputKind { get; set; } = VirtualControllerKind.Xbox360;

    /// <summary>
    /// Optional HIDMaestro catalog profile id (e.g. <c>"xbox-series-xs-bt"</c>,
    /// <c>"switch-pro"</c>, <c>"logitech-g29"</c>). When non-empty, this exact
    /// catalog profile is deployed instead of the default profile derived
    /// from <see cref="OutputKind"/> — which is what lets a slot emit ANY of
    /// HIDMaestro's 225 profiles rather than only the four curated kinds.
    /// Empty means "use the default profile for <see cref="OutputKind"/>".
    /// <see cref="OutputKind"/> is still kept in sync as the profile's
    /// closest family so theming and per-kind UI sections keep working.
    /// </summary>
    public string OutputProfileId { get; set; } = string.Empty;

    /// <summary>
    /// When true, this slot's pipeline is driven by the built-in demo
    /// waveform (the same animated timeline as the "demo" input
    /// provider) INSTEAD of its assigned physical devices — sticks
    /// sweep, triggers pulse, buttons cycle. Everything downstream is
    /// real: the mapping profiles run, the dashboard panels animate, and
    /// the HIDMaestro virtual controller physically emits the motion, so
    /// the virtual pad can be previewed end-to-end (including inside a
    /// game) with no physical controller attached or touched. Excluded
    /// from the output sink's change fingerprint, so the sink itself
    /// never treats a preview toggle as a device change (the slot
    /// registry's save→rebuild cycle still refreshes pipelines on any
    /// slot edit, as it does for every other slot field today).
    /// </summary>
    public bool DemoPreview { get; set; }

    /// <summary>
    /// The output backend this slot uses — a <see cref="ProviderCatalog"/>
    /// key. There is exactly one per platform now (<c>"hidmaestro"</c> on
    /// Windows, <c>"preview"</c> elsewhere), and
    /// <see cref="OutputProviderPolicy"/> resolves EVERY value — current,
    /// legacy <c>"vigem-*"</c>, the old per-slot <c>"preview"</c>, empty,
    /// or unknown — to that backend both at sink creation and at slot
    /// load (see SlotRegistry.Load), so a stale persisted id can never
    /// again land a slot on a silent no-device fallback. Defaults to the
    /// platform backend rather than empty/"inherit" so a brand-new slot
    /// never depends on whatever the profile's own default happens to be.
    /// </summary>
    public string OutputProvider { get; set; } = OutputProviderPolicy.Resolve(null);

    // ── Lighting (DS4/DualSense lightbar) ──
    public bool LightingEnabled { get; set; }
    public byte LightR { get; set; }
    public byte LightG { get; set; }
    public byte LightB { get; set; } = 0xFF;

    // ── Generic DirectInput device shape (OutputKind == GenericDirectInput) ──
    public int ThumbstickCount { get; set; } = 2;
    public int TriggerCount { get; set; } = 2;
    public int PovCount { get; set; } = 1;
    public int ButtonCount { get; set; } = 11;
    public string ProductString { get; set; } = string.Empty;

    /// <summary>
    /// (Vid, Pid) the runtime-built generic device advertises. Defaults
    /// match the custom-profile convention (0xBEEF:0xF000) so a
    /// GameFlow generic pad is recognisable in device lists and never
    /// collides with a real vendor id by accident.
    /// </summary>
    public ushort GenericVendorId { get; set; } = 0xBEEF;
    public ushort GenericProductId { get; set; } = 0xF001;

    /// <summary>Deep copy, used so the editor edits a detached instance until saved.</summary>
    public DeviceOutputTemplate Clone() => (DeviceOutputTemplate)MemberwiseClone();
}
