using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Slots;

/// <summary>
/// Merges the profiles layered onto a slot into a single
/// <see cref="ProfileDocument"/> the slot pipeline can run. Rules from
/// each layer are concatenated in order (deduped by rule id), so a later
/// profile's rules are applied after — and therefore can override — an
/// earlier profile's. An empty layer set yields a neutral profile with no
/// rules (no remapping).
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

        // Keep the first layer's metadata; combine the names for display.
        return layers[0] with
        {
            Name = string.Join(" + ", layers.Select(l => l.Name)),
            Rules = rules,
        };
    }
}
