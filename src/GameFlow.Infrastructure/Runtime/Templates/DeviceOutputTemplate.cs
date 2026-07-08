namespace GameFlow.Infrastructure.Runtime.Templates;

/// <summary>The virtual controller a device's output template emulates.</summary>
public enum VirtualControllerKind
{
    Xbox360 = 0,
    DualShock4,
    DualSense,
    GenericDirectInput,
}

/// <summary>
/// A per-device output template — what virtual controller a physical
/// device should present through HidMaestro, plus its lighting, rumble,
/// force-feedback and adaptive-trigger configuration. The field set is
/// grounded in PadForge's slot configs (ExtendedSlotConfig /
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
    /// The output backend this slot uses — a <see cref="ProviderCatalog"/>
    /// key such as <c>"vigem-xbox360"</c>, <c>"hidmaestro"</c>, or
    /// <c>"preview"</c>. Defaults to a real, working provider rather than
    /// empty/"inherit," so a brand-new slot never silently ends up on
    /// whatever the profile's own default happens to be (historically
    /// "preview" — no real device at all, which reads as broken output
    /// rather than an intentional choice). The per-slot editor is the
    /// only place this is set now: the profile-level picker on the
    /// Dashboard requires an explicit Apply and has no connection to any
    /// individual slot, which was the actual cause of "I picked
    /// HIDMaestro and nothing happened" — the pick never reached this
    /// slot at all. HidMaestro and Preview remain fully supported values
    /// here at the data/sink level; the beta UI just doesn't offer them
    /// as dropdown choices (see DeviceTemplateEditorViewModel).
    /// </summary>
    public string OutputProvider { get; set; } = "vigem-xbox360";

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

    /// <summary>Deep copy, used so the editor edits a detached instance until saved.</summary>
    public DeviceOutputTemplate Clone() => (DeviceOutputTemplate)MemberwiseClone();
}
