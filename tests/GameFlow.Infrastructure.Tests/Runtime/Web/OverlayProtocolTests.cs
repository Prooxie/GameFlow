using System.Text.Json;
using GameFlow.Core.Models;
using GameFlow.Infrastructure.Runtime.Web.Overlay;
using GameFlow.Infrastructure.Theming;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

/// <summary>Tests for <see cref="OverlayProtocol"/>.</summary>
public sealed class OverlayProtocolTests
{
    private static OverlayProgram Program(ThemeRegion? touchRegion) => new()
    {
        ThemeId = "test",
        ThemeName = "Test",
        Width = 400,
        Height = 300,
        Nodes = [],
        ImagePaths = [],
        ValueSlots = [],
        TrailPads = [],
        MissingImages = [],
        TouchRegion = touchRegion
    };

    private static JsonElement Frame(OverlayProgram program, params TouchContact[] contacts)
    {
        var snapshot = ControllerSnapshot.Empty().WithTouchContacts(contacts);
        var frame = OverlayProtocol.BuildFrame(program, new ControllerStateSymbols(), snapshot, null);
        return JsonDocument.Parse(frame).RootElement;
    }

    [Fact]
    public void BuildProgramWithTouchRegionSendsRegion()
    {
        var program = Program(new ThemeRegion(10, 20, 30, 40));

        var json = JsonDocument.Parse(OverlayProtocol.BuildProgram(program)).RootElement;

        var region = json.GetProperty("touchRegion");
        Assert.Equal(10, region.GetProperty("x").GetDouble());
        Assert.Equal(20, region.GetProperty("y").GetDouble());
        Assert.Equal(30, region.GetProperty("width").GetDouble());
        Assert.Equal(40, region.GetProperty("height").GetDouble());
    }

    [Fact]
    public void BuildFrameWithTouchRegionSendsEachContact()
    {
        var program = Program(new ThemeRegion(0, 0, 100, 50));

        var touch = Frame(program, new TouchContact(0, 0.25f, 0.5f, 0.75f), new TouchContact(3, 1f, 0f)).GetProperty("touch");

        Assert.Equal(2, touch.GetArrayLength());
        Assert.Equal([0.25, 0.5, 0, 0.75], touch[0].EnumerateArray().Select(value => value.GetDouble()));
        Assert.Equal([1, 0, 3, 1], touch[1].EnumerateArray().Select(value => value.GetDouble()));
    }

    /// <summary>
    /// An empty list tells the page that every finger is up, so the trails can cool.
    /// </summary>
    [Fact]
    public void BuildFrameWithTouchRegionAndNoContactSendsEmptyList()
    {
        var program = Program(new ThemeRegion(0, 0, 100, 50));

        var touch = Frame(program).GetProperty("touch");

        Assert.Equal(0, touch.GetArrayLength());
    }

    [Fact]
    public void BuildFrameWithoutTouchRegionSendsNoContacts()
    {
        var program = Program(touchRegion: null);

        var frame = Frame(program, new TouchContact(0, 0.5f, 0.5f));

        Assert.False(frame.TryGetProperty("touch", out _));
    }

    [Fact]
    public void BuildFrameOutOfRangeContactIsClamped()
    {
        var program = Program(new ThemeRegion(0, 0, 100, 50));

        var touch = Frame(program, new TouchContact(0, float.NaN, 7f, -2f)).GetProperty("touch");

        Assert.Equal([0, 1, 0, 0], touch[0].EnumerateArray().Select(value => value.GetDouble()));
    }

    /// <summary>
    /// The app draws five finger colours. More contacts only make the frame larger.
    /// </summary>
    [Fact]
    public void BuildFrameManyContactsSendsAtMostFive()
    {
        var program = Program(new ThemeRegion(0, 0, 100, 50));
        var contacts = Enumerable.Range(0, 8).Select(finger => new TouchContact(finger, 0.5f, 0.5f)).ToArray();

        var touch = Frame(program, contacts).GetProperty("touch");

        Assert.Equal(OverlayProtocol.MaxTouchContacts, touch.GetArrayLength());
        Assert.Equal(5, OverlayProtocol.MaxTouchContacts);
    }
}
