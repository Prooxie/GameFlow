using GameFlow.Core.Enums;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using GameFlow.Infrastructure.Runtime.Templates;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime;

/// <summary>
/// Contracts of the kind ↔ HIDMaestro-catalog mapping: each kind has one
/// verified catalog id (the old guess "dualshock4" did not exist in the
/// catalog, which is why DS4 slots never created a device), the generic
/// kind has none (it's built at runtime), and any catalog profile
/// classifies into a kind family that in turn resolves to the dashboard
/// style — the "theme follows the selected output controller" chain.
/// </summary>
public sealed class HidMaestroProfilesTests
{
    [Fact]
    public void Every_selectable_kind_has_a_label()
    {
        foreach (var kind in HidMaestroProfiles.SelectableKinds)
        {
            Assert.False(string.IsNullOrWhiteSpace(HidMaestroProfiles.LabelFor(kind)));
        }
        Assert.Contains(VirtualControllerKind.XboxOne, HidMaestroProfiles.SelectableKinds);
        Assert.Contains(VirtualControllerKind.XboxSeries, HidMaestroProfiles.SelectableKinds);
    }

    [Theory]
    [InlineData("dualsense", null, VirtualControllerKind.DualSense)]
    [InlineData("dualsense-edge", null, VirtualControllerKind.DualSense)]
    [InlineData("dualshock-4-v1-full", null, VirtualControllerKind.DualShock4)]
    [InlineData("dualshock-3", null, VirtualControllerKind.DualShock4)]
    [InlineData("xbox-360-wired", null, VirtualControllerKind.Xbox360)]
    [InlineData("xbox-series-xs-bt", null, VirtualControllerKind.XboxSeries)]
    [InlineData("xbox-one-s-bt", null, VirtualControllerKind.XboxOne)]
    [InlineData("logitech-g29", "Logitech G29 Racing Wheel", VirtualControllerKind.GenericDirectInput)]
    // SwitchPro is a first-class output kind (VirtualControllerKind.SwitchPro),
    // so a Switch Pro classifies as itself rather than falling through to
    // the generic bucket the way it did before that kind existed.
    [InlineData("switch-pro", "Nintendo Switch Pro Controller", VirtualControllerKind.SwitchPro)]
    [InlineData("thrustmaster-t16000m", null, VirtualControllerKind.GenericDirectInput)]
    public void Catalog_profiles_classify_into_kind_families(string id, string? name, VirtualControllerKind expected)
    {
        Assert.Equal(expected, HidMaestroProfiles.ClassifyFamily(id, name));
    }

    [Fact]
    public void Classification_also_reads_the_display_name()
    {
        // An id that says nothing, rescued by the name.
        Assert.Equal(VirtualControllerKind.DualSense,
            HidMaestroProfiles.ClassifyFamily("sony-054c-0ce6", "DualSense Wireless Controller"));
    }

    [Theory]
    [InlineData(VirtualControllerKind.Xbox360, ControllerVisualStyle.Xbox360)]
    [InlineData(VirtualControllerKind.XboxOne, ControllerVisualStyle.XboxOne)]
    [InlineData(VirtualControllerKind.XboxSeries, ControllerVisualStyle.XboxSeries)]
    [InlineData(VirtualControllerKind.DualShock4, ControllerVisualStyle.PlayStation4)]
    [InlineData(VirtualControllerKind.DualSense, ControllerVisualStyle.PlayStation5)]
    // SimpleGamepad, NOT None: None means "render nothing", which is how
    // generic slots ended up as an empty panel with a no-theme message.
    [InlineData(VirtualControllerKind.GenericDirectInput, ControllerVisualStyle.SimpleGamepad)]
    public void Kind_families_resolve_to_dashboard_styles(VirtualControllerKind kind, ControllerVisualStyle expected)
    {
        Assert.Equal(expected, HidMaestroProfiles.ResolveVisualStyle(kind));
    }

    [Fact]
    public void Template_defaults_deploy_the_generic_identity_convention()
    {
        var template = new DeviceOutputTemplate();
        Assert.Equal(0xBEEF, template.GenericVendorId);
        Assert.Equal(0xF001, template.GenericProductId);
        Assert.Equal(string.Empty, template.OutputProfileId);
    }

    [Fact]
    public void Template_clone_carries_the_new_fields()
    {
        var template = new DeviceOutputTemplate
        {
            OutputProfileId = "logitech-g29",
            GenericVendorId = 0x1234,
            GenericProductId = 0x5678,
            DemoPreview = true,
        };

        var clone = template.Clone();

        Assert.Equal("logitech-g29", clone.OutputProfileId);
        Assert.Equal(0x1234, clone.GenericVendorId);
        Assert.Equal(0x5678, clone.GenericProductId);
        Assert.True(clone.DemoPreview);
    }

    [Theory]
    [InlineData(VirtualControllerKind.Xbox360, "xbox-360-wired")]
    [InlineData(VirtualControllerKind.XboxOne, "xbox-one-s-bt")]
    [InlineData(VirtualControllerKind.XboxSeries, "xbox-series-xs-bt")]
    [InlineData(VirtualControllerKind.DualShock4, "dualshock-4-v1-full")]
    [InlineData(VirtualControllerKind.DualSense, "dualsense")]
    [InlineData(VirtualControllerKind.SwitchPro, "switch-pro")]
    [InlineData(VirtualControllerKind.SteamController, "steam-controller")]
    public void DefaultProfileId_IsTheVerifiedCatalogId(VirtualControllerKind kind, string id) =>
        Assert.Equal(id, HidMaestroProfiles.DefaultProfileId(kind));

    [Fact]
    public void DefaultProfileId_Generic_IsNull() =>
        Assert.Null(HidMaestroProfiles.DefaultProfileId(VirtualControllerKind.GenericDirectInput));
}
