using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.Slots;

namespace GameFlow.Infrastructure.Runtime.Effects;

/// <summary>What the tuning editor needs to know about the pad(s) it is editing.</summary>
/// <param name="Features">
/// The capabilities to show settings for. <see cref="ControllerFeatures.None"/>
/// means unknown, and the editor then shows every section.
/// </param>
/// <param name="FamilyNames">Recognised hardware families among the pads, for the summary line. May be empty.</param>
public sealed record ControllerFeatureSummary(ControllerFeatures Features, IReadOnlyList<string> FamilyNames)
{
    public static ControllerFeatureSummary Unknown { get; } = new(ControllerFeatures.None, []);
}

/// <summary>
/// Answers "what can the pad behind this tuning target do", so the Tuning
/// tab can show a DualSense's lightbar and adaptive triggers and hide them
/// from an Xbox pad.
///
/// <para>
/// A target is one device on one slot, or the slot's defaults. Defaults
/// are inherited by every pad on the slot, so they get the UNION of what
/// those pads can do — a setting that only one of two pads has is still
/// worth editing there. If any assigned pad is unknown (offline and never
/// seen this session, so not even its VID/PID is available) the answer is
/// unknown: hiding a section because one pad is missing would hide it for
/// the pad that is present.
/// </para>
/// </summary>
public sealed class ControllerFeatureResolver(InputDeviceCatalog catalog, SlotRegistry slots)
{
    public ControllerFeatureSummary Resolve(string slotId, string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId))
        {
            return ControllerFeatureSummary.Unknown;
        }

        if (!DeviceSettingsStore.IsSlotDefaultsDevice(deviceId))
        {
            return Describe([deviceId]);
        }

        var slot = slots.GetSlots().FirstOrDefault(candidate =>
            string.Equals(candidate.Id, slotId, StringComparison.Ordinal));
        if (slot is null)
        {
            return ControllerFeatureSummary.Unknown;
        }

        return Describe(slot.InputDeviceIds.Where(id => !string.IsNullOrWhiteSpace(id)));
    }

    private ControllerFeatureSummary Describe(IEnumerable<string> deviceIds)
    {
        var features = ControllerFeatures.None;
        var families = new List<string>();
        var any = false;

        foreach (var id in deviceIds)
        {
            if (!catalog.TryGetById(id, out var info) || info is null)
            {
                return ControllerFeatureSummary.Unknown;
            }

            var effective = info.EffectiveFeatures;
            if (effective == ControllerFeatures.None)
            {
                // A keyboard, a mouse or a phone has no identity to look up;
                // say nothing rather than guess.
                return ControllerFeatureSummary.Unknown;
            }

            any = true;
            features |= effective;
            if (ControllerHardwareCatalog.FamilyName(info.VendorId, info.ProductId) is { } family
                && !families.Contains(family, StringComparer.Ordinal))
            {
                families.Add(family);
            }
        }

        return any ? new ControllerFeatureSummary(features, families) : ControllerFeatureSummary.Unknown;
    }
}
