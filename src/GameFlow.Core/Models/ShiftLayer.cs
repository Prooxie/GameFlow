using System.Text.Json.Serialization;
using GameFlow.Core.Enums;

namespace GameFlow.Core.Models;

/// <summary>
/// "Caps Lock for your controller" — a named, colored, optionally
/// emoji-tagged set of extra rules (see <see cref="MappingRule.LayerId"/>)
/// that engages on top of Base according to <see cref="ActivationMode"/>.
///
/// <para>
/// At most ONE non-Base layer is active at a time (Latch's own doc: "press
/// a different Latch button to SWITCH" — not stack). Rules tagged with a
/// layer id apply only while that layer is active; untagged (Base) rules
/// always apply. Whichever rule of a given type/target appears LATER in
/// <see cref="ProfileDocument.Rules"/> wins for that tick — the same
/// last-write-wins convention every other rule interaction in this
/// pipeline already uses — so a layer's remap for South naturally
/// overrides Base's remap for South as long as it's ordered after it.
/// </para>
///
/// <para>
/// A <see cref="ShiftLayerActivationMode.Cycle"/> entry is a special
/// case: it does not itself gate any rule (no rule should reference a
/// Cycle entry's own <see cref="Id"/> as a LayerId). It exists purely to
/// hold <see cref="CycleLayerIds"/> — an ordered queue of OTHER layers
/// (typically <see cref="ShiftLayerActivationMode.NoButton"/> ones) that
/// its <see cref="ActivatorButton"/> steps forward through, with
/// <see cref="CyclePreviousButton"/> stepping back.
/// </para>
/// </summary>
public sealed record ShiftLayer
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; init; } = "Layer";

    /// <summary>Hex color (e.g. "#3B82F6") for the layer's tab/badge.</summary>
    [JsonPropertyName("color")]
    public string Color { get; init; } = "#3B82F6";

    /// <summary>A single emoji shown next to the layer's name.</summary>
    [JsonPropertyName("emoji")]
    public string Emoji { get; init; } = "🎮";

    [JsonPropertyName("activationMode")]
    public ShiftLayerActivationMode ActivationMode { get; init; } = ShiftLayerActivationMode.Hold;

    /// <summary>
    /// The button that engages this layer. Unused (should be
    /// <see cref="ButtonId.None"/>) for <see cref="ShiftLayerActivationMode.NoButton"/>.
    /// For <see cref="ShiftLayerActivationMode.Cycle"/>, this is the
    /// "step forward" control.
    /// </summary>
    [JsonPropertyName("activatorButton")]
    public ButtonId ActivatorButton { get; init; } = ButtonId.None;

    /// <summary>
    /// Toggle/Latch/Sticky only. A press shorter than this is a normal
    /// tap (passed through, un-suppressed, not consumed as a layer
    /// gesture). A press held at least this long flips the layer. 0
    /// (default) = no hold gate; any press immediately flips.
    /// </summary>
    [JsonPropertyName("holdToFireMs")]
    public int HoldToFireMs { get; init; }

    /// <summary>
    /// Toggle only. The layer auto-reverts to Base after this many ms
    /// with no mapped button activity anywhere, so a forgotten toggle
    /// can never strand you off Base. 0 (default) = disabled — stays
    /// on until explicitly toggled off.
    /// </summary>
    [JsonPropertyName("autoCancelMs")]
    public int AutoCancelMs { get; init; }

    /// <summary>Cycle only: ordered layer ids this control steps through.</summary>
    [JsonPropertyName("cycleLayerIds")]
    public IReadOnlyList<string> CycleLayerIds { get; init; } = [];

    /// <summary>Cycle only: steps backward through <see cref="CycleLayerIds"/>. May be bound on the same slot's merged input; a distinct-device binding needs the cross-device work this doesn't include yet.</summary>
    [JsonPropertyName("cyclePreviousButton")]
    public ButtonId CyclePreviousButton { get; init; } = ButtonId.None;

    /// <summary>Cycle only: stepping past the last entry loops to the first (and vice versa for Previous) when true; clamps at the ends when false.</summary>
    [JsonPropertyName("cycleWrapAround")]
    public bool CycleWrapAround { get; init; } = true;

    /// <summary>Cycle only: when true, Base is one of the stops in the rotation (represented as an empty-string entry); when false, the rotation only ever reaches the layers in <see cref="CycleLayerIds"/>.</summary>
    [JsonPropertyName("cycleIncludeBase")]
    public bool CycleIncludeBase { get; init; } = true;
}
