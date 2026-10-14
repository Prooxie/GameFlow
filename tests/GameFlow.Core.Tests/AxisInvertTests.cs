using GameFlow.Core.Models;
using GameFlow.Core.Pipeline;
using Xunit;

namespace GameFlow.Core.Tests;

public sealed class AxisInvertTests
{
    private static ControllerSnapshot PushedUpAndRight() => new()
    {
        LeftStick = new StickVector(0.4f, 0.8f),
        RightStick = new StickVector(0.6f, 0.5f),
        LeftTrigger = 0.25f,
        RightTrigger = 0f,
        Buttons = ButtonState.Clone(ButtonState.CreateEmptyMap())
    };

    [Fact]
    public void OneAxisInverts_WithoutTouchingTheOther()
    {
        // The whole ask: invert Y on one stick and leave everything
        // else — including that stick's own X — exactly as it was.
        var result = AxisInvertProcessor.Apply(
            PushedUpAndRight(), new AxisInvert { RightY = true });

        Assert.Equal(0.6f, result.RightStick.X, precision: 5);
        Assert.Equal(-0.5f, result.RightStick.Y, precision: 5);
        Assert.Equal(0.4f, result.LeftStick.X, precision: 5);
        Assert.Equal(0.8f, result.LeftStick.Y, precision: 5);
    }

    [Fact]
    public void MagnitudeSurvivesInversion()
    {
        // A sign flip is a mirror, not a curve. If inverting changed how
        // far the stick reads, every game with its own deadzone would
        // behave differently inverted than not.
        var snapshot = PushedUpAndRight();
        var result = AxisInvertProcessor.Apply(snapshot, new AxisInvert { RightX = true, RightY = true });

        Assert.Equal(snapshot.RightStick.Magnitude, result.RightStick.Magnitude, precision: 5);
    }

    [Fact]
    public void TriggerInvertsWithinItsOwnRange()
    {
        // Unsigned 0..1, so mirroring is 1-v, not -v — a negative
        // trigger would be clamped straight back to zero.
        var result = AxisInvertProcessor.Apply(
            PushedUpAndRight(), new AxisInvert { LeftTrigger = true, RightTrigger = true });

        Assert.Equal(0.75f, result.LeftTrigger, precision: 5);
        Assert.Equal(1f, result.RightTrigger, precision: 5);
    }

    [Fact]
    public void NothingSet_ReturnsTheSameInstance()
    {
        // This runs per slot per tick at up to 1000 Hz and almost every
        // profile has nothing inverted, so the untouched case has to
        // cost nothing at all.
        var snapshot = PushedUpAndRight();

        Assert.Same(snapshot, AxisInvertProcessor.Apply(snapshot, new AxisInvert()));
        Assert.Same(snapshot, AxisInvertProcessor.Apply(snapshot, AxisInvert.None));
        Assert.Same(snapshot, AxisInvertProcessor.Apply(snapshot, null));
    }

    [Fact]
    public void ExplicitlyFalse_IsNotIdentityConfusedWithInverted()
    {
        var snapshot = PushedUpAndRight();
        var result = AxisInvertProcessor.Apply(snapshot, new AxisInvert { RightY = false });

        Assert.Same(snapshot, result);
    }

    [Fact]
    public void Overlay_LetsALaterLayerTurnAnInversionBackOff()
    {
        // The reason every axis is bool? rather than bool. OR-ing would
        // make a base layer's inversion permanent for anything stacked
        // on top of it.
        var baseLayer = new AxisInvert { RightY = true, LeftX = true };
        var top = new AxisInvert { RightY = false };

        var merged = baseLayer.Overlay(top);

        Assert.False(merged.RightY);
        Assert.True(merged.LeftX);
    }

    [Fact]
    public void Overlay_LeavesAxesTheTopLayerHasNoOpinionAbout()
    {
        var baseLayer = new AxisInvert { RightY = true, LeftTrigger = true, ShowInLayout = true };
        var merged = baseLayer.Overlay(new AxisInvert { LeftX = true });

        Assert.True(merged.RightY);
        Assert.True(merged.LeftTrigger);
        Assert.True(merged.ShowInLayout);
        Assert.True(merged.LeftX);
    }

    [Fact]
    public void Pipeline_InvertsWhatTheGameGetsAndNotWhatTheLayoutShows()
    {
        // The point of the feature: push up, the on-screen stick still
        // shows up, and only the virtual device handed to the game is
        // flipped.
        var profile = new ProfileDocument { Invert = new AxisInvert { RightY = true } };
        using var pipeline = new ControllerMappingPipeline(profile);

        var result = pipeline.Process(PushedUpAndRight(), DateTimeOffset.UtcNow);

        Assert.Equal(0.5f, result.VirtualSnapshot.RightStick.Y, precision: 5);
        Assert.Equal(-0.5f, result.OutputSnapshot.RightStick.Y, precision: 5);
        Assert.Equal(0.5f, result.PhysicalSnapshot.RightStick.Y, precision: 5);
    }

    [Fact]
    public void Pipeline_ShowInLayout_MakesBothSnapshotsAgree()
    {
        var profile = new ProfileDocument
        {
            Invert = new AxisInvert { RightY = true, ShowInLayout = true }
        };
        using var pipeline = new ControllerMappingPipeline(profile);

        var result = pipeline.Process(PushedUpAndRight(), DateTimeOffset.UtcNow);

        Assert.Equal(-0.5f, result.VirtualSnapshot.RightStick.Y, precision: 5);
        Assert.Equal(-0.5f, result.OutputSnapshot.RightStick.Y, precision: 5);

        // The physical panel is never flipped either way — it shows the
        // hand, not the output.
        Assert.Equal(0.5f, result.PhysicalSnapshot.RightStick.Y, precision: 5);
    }

    [Fact]
    public void Pipeline_WithNoInversion_HandsTheSinkTheVirtualSnapshot()
    {
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument());
        var result = pipeline.Process(PushedUpAndRight(), DateTimeOffset.UtcNow);

        Assert.Same(result.VirtualSnapshot, result.OutputSnapshot);
    }
}
