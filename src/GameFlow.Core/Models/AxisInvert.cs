using System.Text.Json.Serialization;

namespace GameFlow.Core.Models;

/// <summary>
/// Per-profile axis inversion — "this GAME wants the camera the other
/// way", as opposed to <see cref="StickSettings.InvertX"/> and friends,
/// which mean "this PAD is wired the other way".
///
/// <para>
/// The distinction is why this is carried by a profile rule
/// (<see cref="Rules.AxisInvertRule"/>) rather than by <c>DeviceSettings</c>. Device tuning is keyed by slot
/// and device and persisted outside any profile, so it deliberately
/// survives a profile switch; a game-specific inversion must not. Both
/// compose, and composing is meaningful: a backwards-wired pad corrected
/// at the device level plus a game that also wants Y flipped cancel out
/// to no flip at all, which is the right answer.
/// </para>
///
/// <para>
/// Every axis is <see cref="bool"/>?, not <see cref="bool"/>, because
/// inversion rules LAYER — across profiles stacked on a slot (see
/// <c>SlotProfileComposer</c>) and across shift layers — and a plain bool
/// cannot express "this rule has no opinion". Without the null an earlier
/// rule's inversion could never be turned back off by one folded on top
/// of it — the two would only ever OR together.
/// </para>
/// </summary>
public sealed record AxisInvert
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("leftX")]
    public bool? LeftX { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("leftY")]
    public bool? LeftY { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rightX")]
    public bool? RightX { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rightY")]
    public bool? RightY { get; init; }

    /// <summary>
    /// Flips a trigger within its own 0..1 range (1 - v), matching
    /// <see cref="TriggerSettings.Invert"/>. Note what that means at
    /// rest: an untouched trigger reads 1, i.e. fully pulled. That is
    /// correct for hardware whose trigger axis runs backwards and is
    /// almost never what a game wants, so it is off by default and kept
    /// separate from the stick toggles in the UI.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("leftTrigger")]
    public bool? LeftTrigger { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rightTrigger")]
    public bool? RightTrigger { get; init; }

    /// <summary>
    /// Whether the on-screen controller shows the inverted signal.
    ///
    /// <para>
    /// Off by default, and deliberately so. The point of a per-game
    /// inversion is that pushing up should still MEAN up — the game is
    /// the thing reading it backwards, not you — so the layout keeps
    /// drawing what your hand is doing and only the virtual device sees
    /// the flip. Turning this on is for anyone who would rather see
    /// exactly what the game receives.
    /// </para>
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("showInLayout")]
    public bool? ShowInLayout { get; init; }

    /// <summary>Nothing inverted, nothing opinionated — what a profile that has never touched this carries.</summary>
    public static AxisInvert None { get; } = new();

    /// <summary>True when applying this would leave a snapshot untouched.</summary>
    [JsonIgnore]
    public bool IsIdentity =>
        LeftX is not true && LeftY is not true
        && RightX is not true && RightY is not true
        && LeftTrigger is not true && RightTrigger is not true;

    /// <summary>
    /// Folds <paramref name="over"/> on top of this one: any axis
    /// <paramref name="over"/> has an opinion about wins, the rest fall
    /// through unchanged. Matches how rule layering already works — a
    /// later rule (and so a later profile in a slot's stack) overrides an
    /// earlier one.
    /// </summary>
    public AxisInvert Overlay(AxisInvert? over)
    {
        if (over is null)
        {
            return this;
        }

        return new AxisInvert
        {
            LeftX = over.LeftX ?? LeftX,
            LeftY = over.LeftY ?? LeftY,
            RightX = over.RightX ?? RightX,
            RightY = over.RightY ?? RightY,
            LeftTrigger = over.LeftTrigger ?? LeftTrigger,
            RightTrigger = over.RightTrigger ?? RightTrigger,
            ShowInLayout = over.ShowInLayout ?? ShowInLayout
        };
    }
}
