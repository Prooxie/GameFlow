using GameFlow.Infrastructure.Runtime.HidMaestro;
using HIDMaestro;
using Xunit;

namespace GameFlow.HidMaestro.Tests;

/// <summary>
/// The generic DirectInput profile is built at run time from a device template's
/// shape. These tests build it against the real SDK, so a changed builder call
/// fails here and not on a user's machine.
/// </summary>
public sealed class HidMaestroGenericProfileTests
{
    private static HMProfile Build(int thumbsticks = 2, int triggers = 2, int buttons = 32, int povs = 1) =>
        HidMaestroGenericProfile.Build(
            "test-pad", new HidMaestroGenericShape("Test", 0xBEEF, 0xF001, thumbsticks, triggers, buttons, povs));

    [WindowsFact]
    public void Build_KeepsIdentity()
    {
        var profile = Build();

        Assert.Equal(0xBEEF, profile.VendorId);
        Assert.Equal(0xF001, profile.ProductId);
        Assert.Equal("test-pad", profile.Id);
        Assert.Equal("Test", profile.ProductString);
    }

    [WindowsFact]
    public void Build_128Buttons_ClampsTo32()
    {
        Assert.Equal(32, Build(buttons: 128).ButtonCount);
    }

    [WindowsFact]
    public void Build_TwoSticksTwoTriggersOneHat()
    {
        var profile = Build();

        Assert.Equal(2, profile.StickCount);
        Assert.Equal(2, profile.TriggerCount);
        Assert.True(profile.HasHat);
    }

    [WindowsFact]
    public void Build_NoPov_HasNoHat()
    {
        Assert.False(Build(povs: 0).HasHat);
    }

    [WindowsFact]
    public void Build_IsDeployable()
    {
        Assert.True(Build().IsDeployable);
    }
}
