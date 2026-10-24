using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using HIDMaestro;
using Xunit;

namespace GameFlow.HidMaestro.Tests;

/// <summary>
/// The writer turns one <see cref="ControllerSnapshot"/> into one
/// <see cref="HMGamepadState"/>. Any field it leaves unwritten still goes out
/// on the wire, so each group of fields has a test.
/// </summary>
public sealed class HidMaestroStateWriterTests
{
    private static HMProfile Profile() =>
        HidMaestroGenericProfile.Build("test-pad", new("Test", 0xBEEF, 0xF001, 2, 2, 32, 1));

    private static HMGamepadState Fill(ControllerSnapshot snapshot, HMProfile? profile = null)
    {
        profile ??= Profile();
        var state = new HMGamepadState();
        new HidMaestroStateWriter(profile).Fill(ref state, snapshot);
        return state;
    }

    private static ControllerSnapshot Pressed(params ButtonId[] buttons)
    {
        var mask = ButtonMask.Empty;
        foreach (var button in buttons) { mask[button] = true; }
        return ControllerSnapshot.Empty("pad").WithButtons(mask);
    }

    [WindowsFact]
    public void Fill_LeftStickFullLeftAndUp_WritesZeroOnBothAxes()
    {
        var profile = Profile();
        var snapshot = ControllerSnapshot.Empty("pad") with { LeftStick = new StickVector(-1f, -1f) };

        var state = Fill(snapshot, profile);

        Assert.Equal(0f, state.Axes![profile.Sticks[0].XAxis]);
        Assert.Equal(0f, state.Axes![profile.Sticks[0].YAxis]);
    }

    [WindowsFact]
    public void Fill_NonFiniteStick_WritesNaN()
    {
        var profile = Profile();
        var snapshot = ControllerSnapshot.Empty("pad") with { RightStick = new StickVector(float.PositiveInfinity, 0f) };

        var state = Fill(snapshot, profile);

        Assert.True(float.IsNaN(state.Axes![profile.Sticks[1].XAxis]));
    }

    [WindowsFact]
    public void Fill_LeftTrigger_WritesItsAxis()
    {
        var profile = Profile();
        var snapshot = ControllerSnapshot.Empty("pad") with { LeftTrigger = 0.25f };

        var state = Fill(snapshot, profile);

        Assert.Equal(0.25f, state.Axes![profile.Triggers[0].Axis]);
    }

    [WindowsFact]
    public void Fill_SouthAndStart_SetAAndStart()
    {
        var state = Fill(Pressed(ButtonId.South, ButtonId.Start));

        Assert.Equal(HMButton.A | HMButton.Start, state.Buttons);
    }

    [WindowsFact]
    public void Fill_UpAndRight_SetsHatNorthEast()
    {
        var state = Fill(Pressed(ButtonId.DpadUp, ButtonId.DpadRight));

        Assert.Equal(HMHat.NorthEast, state.Hat);
    }

    [WindowsFact]
    public void Fill_SourceWithoutGyro_ZeroesMotion()
    {
        var state = new HMGamepadState
        {
            AccelGX = 1,
            AccelGY = 1,
            AccelGZ = 1,
            GyroDpsX = 1,
            GyroDpsY = 1,
            GyroDpsZ = 1,
        };

        new HidMaestroStateWriter(Profile()).Fill(ref state, ControllerSnapshot.Empty("pad"));

        Assert.Equal(0f, state.AccelGX);
        Assert.Equal(0f, state.AccelGY);
        Assert.Equal(0f, state.AccelGZ);
        Assert.Equal(0f, state.GyroDpsX);
        Assert.Equal(0f, state.GyroDpsY);
        Assert.Equal(0f, state.GyroDpsZ);
    }

    [WindowsFact]
    public void Fill_TouchAtCentre_Writes960And540()
    {
        var snapshot = ControllerSnapshot.Empty("pad").WithTouchContacts(
            [new TouchContact(FingerIndex: 0, X: 0.5f, Y: 0.5f, Pressure: 1f)]);

        var state = Fill(snapshot);

        Assert.True(state.TouchpadFinger0Active);
        Assert.Equal(960, state.TouchpadFinger0X);
        Assert.Equal(540, state.TouchpadFinger0Y);
        Assert.False(state.TouchpadFinger1Active);
    }

    [WindowsFact]
    public void Fill_Battery_WritesLevelChargingAndFull()
    {
        var snapshot = ControllerSnapshot.Empty("pad") with { BatteryPercent = 40, BatteryCharging = true };

        var state = Fill(snapshot);

        Assert.Equal(4, state.BatteryLevel);
        Assert.True(state.BatteryCharging);
        Assert.False(state.BatteryFull);
        Assert.True(Fill(ControllerSnapshot.Empty("pad")).BatteryFull);
    }

    [WindowsFact]
    public void ButtonMap_TouchpadClickIsTouchpadNotShare()
    {
        var entry = Assert.Single(HidMaestroStateWriter.ButtonMap, e => e.Source == ButtonId.Touchpad);

        Assert.Equal(HMButton.Touchpad, entry.Target);
    }

    [WindowsTheory]
    [InlineData(ButtonId.Paddle1, HMButton.LeftPaddle)]
    [InlineData(ButtonId.Paddle2, HMButton.RightPaddle)]
    [InlineData(ButtonId.Paddle3, HMButton.LeftPaddle2)]
    [InlineData(ButtonId.Paddle4, HMButton.RightPaddle2)]
    public void ButtonMap_PaddlesPairBySide(ButtonId source, HMButton target)
    {
        var entry = Assert.Single(HidMaestroStateWriter.ButtonMap, e => e.Source == source);

        Assert.Equal(target, entry.Target);
    }

    [WindowsFact]
    public void ButtonMap_HasNoDuplicateSourcesOrTargets()
    {
        Assert.Equal(17, HidMaestroStateWriter.ButtonMap.Count);
        Assert.Equal(17, HidMaestroStateWriter.ButtonMap.Select(e => e.Source).Distinct().Count());
        Assert.Equal(17, HidMaestroStateWriter.ButtonMap.Select(e => e.Target).Distinct().Count());
    }

    [WindowsFact]
    public void Fill_ProfileWithoutSticksOrTriggers_FollowsAxisMap()
    {
        // The Switch 2 Pro profiles put the right trigger on Ry through
        // axisMap. Writing it to the default Rz would overwrite a stick axis.
        var descriptor = new HidDescriptorBuilder().Gamepad().AddButtons(8);
        var profile = new HMProfileBuilder()
            .Id("axis-map-pad").Name("Axis map").Vendor("GameFlow").Vid(0xBEEF).Pid(0xF002)
            .ProductString("Axis map").ManufacturerString("GameFlow").Type("gamepad").Connection("usb")
            .FromDescriptorBuilder(descriptor)
            .AxisMap(new() { ["0x34"] = "rightTrigger" })
            .Build();
        var snapshot = ControllerSnapshot.Empty("pad") with { RightTrigger = 0.75f };

        var state = Fill(snapshot, profile);

        Assert.Equal(0.75f, state.Axes![HMAxis.Ry]);
        Assert.False(state.Axes.TryGetValue(HMAxis.Rz, out var rz) && rz == 0.75f);
    }

    [WindowsFact]
    public void Fill_SteadyState_AllocatesNothing()
    {
        var writer = new HidMaestroStateWriter(Profile());
        var snapshot = Pressed(ButtonId.South, ButtonId.DpadUp) with
        {
            LeftStick = new StickVector(0.3f, -0.2f),
            LeftTrigger = 0.5f,
            BatteryPercent = 80,
        };
        var state = new HMGamepadState();
        writer.Fill(ref state, snapshot);

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) { writer.Fill(ref state, snapshot); }
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(before, after);
    }
}
