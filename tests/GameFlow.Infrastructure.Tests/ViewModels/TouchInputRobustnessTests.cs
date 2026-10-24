using GameFlow.App.ViewModels;
using GameFlow.App.Views;
using GameFlow.Core.Models;
using Xunit;

namespace GameFlow.Infrastructure.Tests.ViewModels;

public sealed class TouchInputRobustnessTests
{
    [Fact]
    public void ChangingFingerIdentifiersDoesNotRetainExpiredTrackingEntries()
    {
        var trail = new TouchTrail();
        var particles = new TouchParticles(seed: 1);
        for (var i = 0; i < 2000; i++)
        {
            TouchContact[] contacts = [new(i, .5f, .5f)];
            trail.Record(contacts, i * .1);
            particles.Emit(contacts, i * .1);
            Assert.InRange(trail.TrackedFingerCount, 1, 7);
            Assert.InRange(particles.TrackedFingerCount, 1, 11);
        }
        trail.Prune(205);
        particles.Prune(205);
        Assert.Equal(0, trail.TrackedFingerCount);
        Assert.Equal(0, particles.TrackedFingerCount);
        Assert.False(trail.HasPoints);
        Assert.Empty(particles.Particles);
    }

    [Fact]
    public void NonFiniteContactsCannotPoisonTheTouchEffects()
    {
        TouchContact[] contacts = [new(0, float.NaN, .5f), new(1, .5f, float.PositiveInfinity), new(2, .5f, .5f, float.NaN)];
        var trail = new TouchTrail();
        var particles = new TouchParticles(seed: 1);
        trail.Record(contacts, 0);
        particles.Emit(contacts, 0);
        Assert.False(trail.HasPoints);
        Assert.Empty(particles.Particles);
        TouchContact[] valid = [new(0, 0, 1)];
        trail.Record(valid, .1);
        particles.Emit(valid, .1);
        Assert.Single(trail.Samples(0));
        Assert.Single(particles.Particles);
    }

    [Fact]
    public void NonFiniteMouseMotionLeavesTheNativeArrowAtItsCentre()
    {
        Assert.Equal(new Avalonia.Point(30, 30), ControllerVisualStateViewModel.AimEndPointFor(new(float.NaN, float.PositiveInfinity)));
    }
}
