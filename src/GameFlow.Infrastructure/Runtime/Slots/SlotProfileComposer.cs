using GameFlow.Core.Models;

namespace GameFlow.Infrastructure.Runtime.Slots;

/// <summary>
/// Merges the profiles layered onto a slot into a single
/// <see cref="ProfileDocument"/> the slot pipeline can run. Rules from
/// each layer are concatenated in order, so a later profile's rules are
/// applied after — and therefore can override — an earlier profile's. An
/// empty layer set yields a neutral profile with no rules (no remapping).
///
/// <para>
/// <b>Equal rule ids: the later layer wins.</b> When two layers carry a
/// rule with the same id, the later layer's copy is kept, at its own
/// position, and the earlier one is dropped. Ids repeat across profiles
/// in practice — every profile created from the defaults starts with the
/// same rule ids, and a duplicated profile keeps its source's — so this
/// is what makes editing such a rule in the TOP profile take effect. It
/// used to be first-wins, which silently threw the top profile's edit
/// away and kept the bottom one's.
/// </para>
///
/// <para>
/// Axis inversion needs no special handling any more: it is an ordinary
/// <see cref="GameFlow.Core.Models.Rules.AxisInvertRule"/> per profile,
/// with a per-profile id, and the pipeline folds those per axis in list
/// order — so a later layer still overrides an earlier one axis by axis,
/// exactly as the old per-profile header did.
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

        // Two passes: find each id's LAST occurrence, then keep exactly
        // that one, in stack order.
        var lastById = new Dictionary<string, MappingRule>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            foreach (var rule in layer.Rules)
            {
                lastById[rule.Id] = rule;
            }
        }

        var rules = new List<MappingRule>(lastById.Count);
        var added = new HashSet<string>(StringComparer.Ordinal);
        foreach (var layer in layers)
        {
            foreach (var rule in layer.Rules)
            {
                // The id check as well as the reference check: the same
                // profile instance listed twice must still add its rules once.
                if (ReferenceEquals(lastById[rule.Id], rule) && added.Add(rule.Id))
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
            Invert = null,
        };
    }
}
