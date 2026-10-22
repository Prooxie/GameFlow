using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.HidMaestro;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.HidMaestro;

/// <summary>
/// The unit conversions shared by the sink and the typed HIDMaestro adapter.
/// </summary>
public sealed class HidMaestroSubmissionTests
{
    [Theory]
    [InlineData(-1f, 0f)]
    [InlineData(0f, 0.5f)]
    [InlineData(1f, 1f)]
    [InlineData(-3f, 0f)]
    [InlineData(3f, 1f)]
    public void ToAxis_MapsSignedRangeOntoSdkRange(float input, float expected) =>
        Assert.Equal(expected, HidMaestroSubmission.ToAxis(input));

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ToAxis_NonFinite_LeavesTheAxisToTheSdkRest(float input) =>
        Assert.True(float.IsNaN(HidMaestroSubmission.ToAxis(input)));

    [Theory]
    [InlineData(0.25f, 0.25f)]
    [InlineData(2f, 1f)]
    [InlineData(-1f, 0f)]
    public void ToTrigger_Clamps(float input, float expected) =>
        Assert.Equal(expected, HidMaestroSubmission.ToTrigger(input));

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ToTrigger_NonFinite_LeavesTheTriggerToTheSdkRest(float input) =>
        Assert.True(float.IsNaN(HidMaestroSubmission.ToTrigger(input)));

    [Fact]
    public void ResolveHat_UpAndRight_IsNorthEast() =>
        Assert.Equal(HatDirection.NorthEast, HidMaestroSubmission.ResolveHat(Pressed(ButtonId.DpadUp, ButtonId.DpadRight)));

    [Fact]
    public void ResolveHat_Down_IsSouth() =>
        Assert.Equal(HatDirection.South, HidMaestroSubmission.ResolveHat(Pressed(ButtonId.DpadDown)));

    [Fact]
    public void ResolveHat_Nothing_IsNone() =>
        Assert.Equal(HatDirection.None, HidMaestroSubmission.ResolveHat(ControllerSnapshot.Empty("pad")));

    private static ControllerSnapshot Pressed(params ButtonId[] buttons)
    {
        var mask = ButtonMask.Empty;
        foreach (var button in buttons)
        {
            mask[button] = true;
        }

        return ControllerSnapshot.Empty("pad").WithButtons(mask);
    }
}
