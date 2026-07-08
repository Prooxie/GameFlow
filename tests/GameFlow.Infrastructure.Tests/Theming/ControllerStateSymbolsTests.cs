using Autofire.Core.Enums;
using Autofire.Core.Models;
using Autofire.Infrastructure.Theming;
using Xunit;

namespace Autofire.Infrastructure.Tests.Theming;

/// <summary>
/// Verifies the mapping from <see cref="ControllerSnapshot"/> fields to
/// VSCView's colon-qualified Flee variable namespace. The tests cover
/// the full surface area listed in
/// <see href="https://github.com/Nielk1/VSCView/blob/master/THEMEENGINE.md">THEMEENGINE.md</see>
/// so any future change to either side has to update one of these cases.
/// </summary>
public sealed class ControllerStateSymbolsTests
{
    /// <summary>
    /// Builds a snapshot with every interesting field set so each
    /// resolver branch has a value to read.
    /// </summary>
    private static ControllerSnapshot MakeSnapshot(
        bool pressA = false, bool pressB = false, bool pressX = false, bool pressY = false,
        bool dpadUp = false, bool dpadDown = false, bool dpadLeft = false, bool dpadRight = false,
        bool ls = false, bool rs = false, bool guide = false,
        float lt = 0f, float rt = 0f,
        float lx = 0f, float ly = 0f, float rx = 0f, float ry = 0f)
    {
        var s = ControllerSnapshot.Empty();
        if (pressA)     { s = s.WithButton(ButtonId.South, true); }
        if (pressB)     { s = s.WithButton(ButtonId.East,  true); }
        if (pressX)     { s = s.WithButton(ButtonId.West,  true); }
        if (pressY)     { s = s.WithButton(ButtonId.North, true); }
        if (dpadUp)     { s = s.WithButton(ButtonId.DpadUp,    true); }
        if (dpadDown)   { s = s.WithButton(ButtonId.DpadDown,  true); }
        if (dpadLeft)   { s = s.WithButton(ButtonId.DpadLeft,  true); }
        if (dpadRight)  { s = s.WithButton(ButtonId.DpadRight, true); }
        if (ls)         { s = s.WithButton(ButtonId.LeftStick,  true); }
        if (rs)         { s = s.WithButton(ButtonId.RightStick, true); }
        if (guide)      { s = s.WithButton(ButtonId.Guide,      true); }
        s = s with
        {
            LeftTrigger = lt,
            RightTrigger = rt,
            LeftStick = new StickVector(lx, ly),
            RightStick = new StickVector(rx, ry)
        };
        return s;
    }

    /// <summary>
    /// Face-button mapping is fixed: south→A/X, east→B/○, west→X/□,
    /// north→Y/△ regardless of controller family.
    /// </summary>
    [Theory]
    [InlineData("quad_right:s", true,  false, false, false, 1)]
    [InlineData("quad_right:e", false, true,  false, false, 1)]
    [InlineData("quad_right:w", false, false, true,  false, 1)]
    [InlineData("quad_right:n", false, false, false, true,  1)]
    [InlineData("quad_right:s", false, false, false, false, 0)]
    public void Resolves_face_buttons(string name, bool a, bool b, bool x, bool y, double expected)
    {
        var symbols = new ControllerStateSymbols();
        symbols.UpdateSnapshot(MakeSnapshot(pressA: a, pressB: b, pressX: x, pressY: y));
        Assert.Equal(expected, symbols.Resolve(name));
    }

    /// <summary>
    /// Trigger values are reported verbatim for <c>:analog</c> and
    /// thresholded at 0.95 for <c>:stage2</c>. The threshold is our
    /// stand-in for Steam Controller hardware that VSCView's spec
    /// describes; non-SC hardware lights up stage2 only at a hard pull.
    /// </summary>
    [Theory]
    [InlineData(0.0f, 0.0)]
    [InlineData(0.5f, 0.5)]
    [InlineData(1.0f, 1.0)]
    public void Resolves_analog_triggers(float pull, double expected)
    {
        var symbols = new ControllerStateSymbols();
        symbols.UpdateSnapshot(MakeSnapshot(lt: pull, rt: pull));
        Assert.Equal(expected, symbols.Resolve("triggers:l:analog"), 3);
        Assert.Equal(expected, symbols.Resolve("triggers:r:analog"), 3);
    }

    [Theory]
    [InlineData(0.5f, 0)]
    [InlineData(0.9f, 0)]
    [InlineData(0.96f, 1)]
    [InlineData(1.0f, 1)]
    public void Resolves_stage2_triggers(float pull, int expected)
    {
        var symbols = new ControllerStateSymbols();
        symbols.UpdateSnapshot(MakeSnapshot(lt: pull));
        Assert.Equal(expected, symbols.Resolve("triggers:l:stage2"));
    }

    /// <summary>
    /// Stick Y is reported with a sign flip so VSCView's screen-Y
    /// convention (positive down) matches our Steam-Input convention
    /// (positive up). A snapshot LY of +0.5 should resolve as -0.5.
    /// </summary>
    [Fact]
    public void Flips_stick_y_to_screen_convention()
    {
        var symbols = new ControllerStateSymbols();
        symbols.UpdateSnapshot(MakeSnapshot(lx: 0.3f, ly: 0.5f));
        Assert.Equal( 0.3, symbols.Resolve("stick_left:x"), 5);
        Assert.Equal(-0.5, symbols.Resolve("stick_left:y"), 5);
    }

    /// <summary>
    /// Stick clicks resolve through L3 / R3, distinct from positional
    /// axes.
    /// </summary>
    [Fact]
    public void Resolves_stick_click()
    {
        var symbols = new ControllerStateSymbols();
        symbols.UpdateSnapshot(MakeSnapshot(ls: true, rs: false));
        Assert.Equal(1, symbols.Resolve("stick_left:click"));
        Assert.Equal(0, symbols.Resolve("stick_right:click"));
    }

    /// <summary>
    /// Unknown names resolve to 0, never throw. This is what lets a
    /// theme written for a DualSense (which references <c>mute</c>)
    /// render on an Xbox controller — the mute element just stays off.
    /// </summary>
    [Theory]
    [InlineData("totally_made_up")]
    [InlineData("quad_right:xyz")]
    [InlineData("motion:gyro:x")]
    public void Returns_zero_for_unknown_names(string name)
    {
        var symbols = new ControllerStateSymbols();
        symbols.UpdateSnapshot(MakeSnapshot());
        Assert.Equal(0, symbols.Resolve(name));
    }
}
