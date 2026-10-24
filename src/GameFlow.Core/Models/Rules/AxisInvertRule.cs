using System.Text.Json.Serialization;

namespace GameFlow.Core.Models.Rules;

/// <summary>
/// Per-game axis inversion as an ordinary rule — "this GAME wants the
/// camera the other way". It used to be a fixed card in the profile
/// header (<see cref="ProfileDocument.Invert"/>); as a rule it gets what
/// every other rule already has: an enabled switch, a shift layer, a
/// rule toggle that can turn it on and off mid-game, and a place on the
/// stick and trigger cards.
///
/// <para>
/// It still runs LAST, after every other rule, in a pass of its own (see
/// <see cref="Pipeline.ControllerMappingPipeline"/>), so no rule ever
/// reads an inverted stick: adding an inversion never silently reverses
/// the sense of a rule written before it. Rule order matters only between
/// inversion rules — they fold with <see cref="AxisInvert.Overlay"/> in
/// list order, so a later rule's explicit opinion about an axis wins and
/// its "no opinion" (null) defers to whatever came before. That is the
/// same fold the old header used across layered profiles, which is why
/// profiles converted from it keep their meaning exactly.
/// </para>
/// </summary>
public sealed record AxisInvertRule : MappingRule
{
    /// <summary>
    /// Which axes to flip. Every axis is nullable for the reason given on
    /// <see cref="AxisInvert"/>: null means "no opinion", so a rule
    /// stacked on top of another can switch one axis back off without
    /// restating the others.
    /// </summary>
    [JsonPropertyName("axes")]
    public AxisInvert Axes { get; init; } = new();
}
