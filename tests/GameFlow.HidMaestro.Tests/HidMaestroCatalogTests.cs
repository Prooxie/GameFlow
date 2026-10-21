using GameFlow.Infrastructure.Runtime.HidMaestro;
using GameFlow.Infrastructure.Runtime.Templates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.HidMaestro.Tests;

/// <summary>
/// The catalog comes from the SDK's embedded profiles. These tests read it
/// through a real session, so a renamed profile or a changed identity in a
/// new SDK fails here and not on a user's machine.
/// </summary>
public sealed class HidMaestroCatalogTests
{
    // One session for the class: each HMContext starts its own warm-up work.
    private static readonly Lazy<HidMaestroSession> Session = new(() => new HidMaestroSession(NullLogger.Instance));

    private static HidMaestroCatalogProfile Profile(string id) =>
        Assert.Single(Session.Value.Catalog, p => p.Id == id);

    [WindowsFact]
    public void EveryKindDefault_IsADeployableCatalogProfile()
    {
        foreach (var kind in Enum.GetValues<VirtualControllerKind>())
        {
            if (kind == VirtualControllerKind.GenericDirectInput)
            {
                continue;
            }

            var id = HidMaestroProfiles.DefaultProfileId(kind);
            Assert.NotNull(id);
            var profile = Session.Value.Catalog.SingleOrDefault(p => p.Id == id);
            Assert.True(profile is not null, $"{kind}: '{id}' is not in the catalog.");
            Assert.True(profile.IsDeployable, $"{kind}: '{id}' is not deployable.");
        }
    }

    [WindowsTheory]
    [InlineData("dualshock-4-v1-full", 0x054C, 0x05C4)]
    [InlineData("xbox-one-s-bt", 0x045E, 0x02FD)]
    [InlineData("dualsense", 0x054C, 0x0CE6)]
    public void KindDefaults_CarryTheirRealSignatures(string id, int vendorId, int productId)
    {
        var profile = Profile(id);

        Assert.Equal(vendorId, profile.VendorId);
        Assert.Equal(productId, profile.ProductId);
    }

    [WindowsFact]
    public void Project_CarriesTypeAndExtendedInput()
    {
        Assert.True(Profile("dualsense-bt").HasExtendedInput);
        // In SDK 2.0.1 the USB profile declares an extended report too, with no armOn.
        Assert.True(Profile("dualsense").HasExtendedInput);
        Assert.False(Profile("xbox-360-wired").HasExtendedInput);
        Assert.Equal("gamepad", Profile("xbox-360-wired").Type);
        Assert.Equal("wheel", Profile("asetek-forte").Type);
    }
}
