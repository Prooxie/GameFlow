using Autofire.Infrastructure.Runtime.Templates;

namespace Autofire.Infrastructure.Runtime.HidMaestro;

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
}
