using GameFlow.Infrastructure.Runtime.Templates;

namespace GameFlow.Infrastructure.Runtime.HidMaestro;

/// <summary>
/// Maps an Autofire <see cref="VirtualControllerKind"/> to the HIDMaestro
/// catalog profile id its SDK uses (the string passed to
/// <c>HMContext.GetProfile(id)</c>). Ids follow HIDMaestro's catalog
/// slugs; adjust to match the exact slugs shipped by the SDK version you
/// reference (PadForge's <c>HMaestroProfileCatalog</c> is the source of
/// truth for the full list).
/// </summary>
public static class HidMaestroProfiles
{
    public static string ResolveProfileId(VirtualControllerKind kind) => kind switch
    {
        VirtualControllerKind.Xbox360 => "xbox-360-wired",
        VirtualControllerKind.DualShock4 => "dualshock4",
        VirtualControllerKind.DualSense => "dualsense",
        // The generic DirectInput device is authored at runtime via
        // HMProfileBuilder/HidDescriptorBuilder rather than a catalog
        // slug; callers detect this kind and build the descriptor from
        // the template's axis/button/POV counts.
        VirtualControllerKind.GenericDirectInput => "custom",
        _ => "xbox-360-wired",
    };

    /// <summary>
    /// The (Vid, Pid) HIDMaestro's emitted device presents for a given
    /// kind — the same well-known pairs ViGEm's sinks use, since a
    /// virtual controller has to advertise the real console/manufacturer
    /// identity for games and Steam to recognize it as that controller
    /// type in the first place. This is what lets the runtime hide
    /// HIDMaestro's own output from the input device list, the same
    /// protection ViGEm outputs already had — without it, this specific
    /// backend could be selected as its own input, the exact class of
    /// bug the ViGEm-side hiding exists to prevent. Null for
    /// GenericDirectInput, which has no fixed real-world identity to
    /// impersonate.
    /// </summary>
    public static (ushort Vid, ushort Pid)? ResolveHardwareSignature(VirtualControllerKind kind) => kind switch
    {
        VirtualControllerKind.Xbox360 => (0x045E, 0x028E),
        VirtualControllerKind.DualShock4 => (0x054C, 0x09CC),
        VirtualControllerKind.DualSense => (0x054C, 0x09CC),
        _ => null,
    };
}
