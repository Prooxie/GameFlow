using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Slots;

/// <summary>
/// Merges the profiles layered onto a slot into a single
/// <see cref="ProfileDocument"/> the slot pipeline can run. Rules from
/// each layer are concatenated in order (deduped by rule id), so a later
/// profile's rules are applied after — and therefore can override — an
/// earlier profile's. An empty layer set yields a neutral profile with no
/// rules (no remapping).
///
/// <para>
/// <see cref="ProfileDocument.Invert"/> follows the same
/// later-layer-wins rule, per axis. It is folded rather than OR'd
/// precisely so a layer CAN turn an inversion back off — see the note on
/// <see cref="AxisInvert"/> about why each axis is nullable.
/// </para>
/// </summary>
public static class SlotProfileComposer
{
    /// <summary>A neutral profile with no rules.</summary>
    public static ProfileDocument Empty { get; } = new()
    {
        Id = "(empty)",
        Name = "(empty)",
        Rules = [],
    };

    public static ProfileDocument Compose(IReadOnlyList<ProfileDocument> layers)
    {
        if (layers is null || layers.Count == 0)
        {
            return Empty;
        }
        if (layers.Count == 1)
        {
            return layers[0];
        }

        var rules = new List<MappingRule>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var invert = layers[0].Invert ?? AxisInvert.None;
        foreach (var layer in layers)
        {
            foreach (var rule in layer.Rules)
            {
                if (seen.Add(rule.Id))
                {
                    rules.Add(rule);
                }
            }
        }

        for (var i = 1; i < layers.Count; i++)
        {
            invert = invert.Overlay(layers[i].Invert);
        }

        // Keep the first layer's metadata; combine the names for display.
        return layers[0] with
        {
            Name = string.Join(" + ", layers.Select(l => l.Name)),
            Rules = rules,
            Invert = invert,
        };
    }
}
