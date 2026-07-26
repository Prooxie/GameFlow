using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using GameFlow.Core.Pipeline;
using Xunit;

namespace GameFlow.Core.Tests;

public sealed class GyroProcessorTests
{
    private static ControllerSnapshot Motion(
        float pitch = 0f, float yaw = 0f, float roll = 0f,
        float accelX = 0f, float accelY = 9.81f, float accelZ = 0f,
        ButtonId? pressed = null,
        StickVector? leftStick = null)
    {
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        if (pressed is { } id)
        {
            buttons[id] = true;
        }
        return new ControllerSnapshot
        {
            Buttons = buttons,
            LeftStick = leftStick ?? StickVector.Zero,
            HasGyro = true,
            GyroPitch = pitch,
            GyroYaw = yaw,
            GyroRoll = roll,
            AccelX = accelX,
            AccelY = accelY,
            AccelZ = accelZ
        };
    }

    /// <summary>Smoothing off, so tests measure the projection rather than the filter.</summary>
    private static GyroMapRule Rule(GyroReferenceFrame frame = GyroReferenceFrame.Local) => new()
    {
        Id = "gyro",
        ReferenceFrame = frame,
        EngageMode = GyroEngageMode.AlwaysOn,
        SmoothingLowerThreshold = 0f,
        DeadzoneRadiansPerSecond = 0f
    };

    [Fact]
    public void TurningLeft_AimsLeft()
    {
        // SDL: positive yaw is counter-clockwise about the up axis =
        // nose turning LEFT. Aiming left is negative horizontal.
        var result = new GyroProcessor().Process(Rule(), Motion(yaw: 1.0f));

        Assert.True(result.Engaged);
        Assert.True(result.Horizontal < 0f);
    }

    [Fact]
    public void TiltingNoseUp_AimsUp()
    {
        // Positive pitch rotates the pad's top toward you = nose rises.
        var result = new GyroProcessor().Process(Rule(), Motion(pitch: 1.0f));

        Assert.True(result.Vertical > 0f);
    }

    [Fact]
    public void InvertFlipsEachAxisIndependently()
    {
        var rule = Rule() with { InvertX = true };
        var result = new GyroProcessor().Process(rule, Motion(yaw: 1.0f, pitch: 1.0f));

        Assert.True(result.Horizontal > 0f); // inverted
        Assert.True(result.Vertical > 0f);   // untouched
    }

    [Fact]
    public void SensitivityScalesOutputLinearly()
    {
        var baseResult = new GyroProcessor().Process(Rule(), Motion(yaw: 1.0f));
        var doubled = new GyroProcessor().Process(Rule() with { SensitivityX = 2.0f }, Motion(yaw: 1.0f));

        Assert.Equal(baseResult.Horizontal * 2f, doubled.Horizontal, precision: 4);
    }

    [Fact]
    public void BiasCorrectionCancelsSteadyDrift()
    {
        // A pad reporting 0.05 rad/s while sitting still should read as
        // completely stationary once its bias is calibrated out.
        var rule = Rule() with { BiasYaw = 0.05f };
        var result = new GyroProcessor().Process(rule, Motion(yaw: 0.05f));

        Assert.Equal(0f, result.Horizontal, precision: 5);
    }

    [Fact]
    public void DeadzoneSuppressesResidualCreep()
    {
        var rule = Rule() with { DeadzoneRadiansPerSecond = 0.1f };
        var result = new GyroProcessor().Process(rule, Motion(yaw: 0.05f));

        Assert.Equal(0f, result.Horizontal, precision: 5);
    }

    [Fact]
    public void LocalFrame_IgnoresHowThePadIsTilted()
    {
        // Same yaw input, pad rolled onto its side: Local doesn't care.
        var upright = new GyroProcessor().Process(Rule(), Motion(yaw: 1.0f, accelX: 0f, accelY: 9.81f));
        var rolled = new GyroProcessor().Process(Rule(), Motion(yaw: 1.0f, accelX: 9.81f, accelY: 0f));

        Assert.Equal(upright.Horizontal, rolled.Horizontal, precision: 4);
    }

    [Fact]
    public void WorldFrame_UsesPitchAsHorizontalWhenThePadIsRolledSideways()
    {
        // Rolled 90°, the pad's pitch axis now points along world-up, so
        // rotating about pitch is what turns you horizontally.
        var rule = Rule(GyroReferenceFrame.World);
        var result = new GyroProcessor().Process(rule, Motion(pitch: 1.0f, accelX: 9.81f, accelY: 0f));

        Assert.True(MathF.Abs(result.Horizontal) > 0.5f);
    }

    [Fact]
    public void PlayerFrame_CombinesYawAndRollWhenLeaning()
    {
        // Leaning the pad 45° means part of a "turn" registers as roll —
        // Player space folds that back into horizontal aim, so the
        // combined input reads stronger than yaw alone would.
        var lean = 0.7071f * 9.81f;
        var rule = Rule(GyroReferenceFrame.Player);

        var yawOnly = new GyroProcessor().Process(rule, Motion(yaw: 1.0f, accelY: lean, accelZ: lean));
        var yawAndRoll = new GyroProcessor().Process(rule, Motion(yaw: 1.0f, roll: 1.0f, accelY: lean, accelZ: lean));

        Assert.True(MathF.Abs(yawAndRoll.Horizontal) > MathF.Abs(yawOnly.Horizontal));
    }

    [Fact]
    public void PlayerAndWorldFramesFallBackToLocal_WhenThereIsNoAccelerometer()
    {
        var local = new GyroProcessor().Process(Rule(GyroReferenceFrame.Local), Motion(yaw: 1.0f, accelY: 0f));
        var player = new GyroProcessor().Process(Rule(GyroReferenceFrame.Player), Motion(yaw: 1.0f, accelY: 0f));
        var world = new GyroProcessor().Process(Rule(GyroReferenceFrame.World), Motion(yaw: 1.0f, accelY: 0f));

        Assert.Equal(local.Horizontal, player.Horizontal, precision: 4);
        Assert.Equal(local.Horizontal, world.Horizontal, precision: 4);
    }

    [Fact]
    public void NoGyroHardware_ProducesNothing()
    {
        var frame = new ControllerSnapshot { Buttons = ButtonState.CreateEmptyMap(), HasGyro = false, GyroYaw = 5f };
        var result = new GyroProcessor().Process(Rule(), frame);

        Assert.False(result.Engaged);
        Assert.Equal(0f, result.Horizontal, precision: 5);
    }

    [Fact]
    public void HoldToEngage_OnlyProducesOutputWhileTheButtonIsHeld()
    {
        var rule = Rule() with { EngageMode = GyroEngageMode.HoldToEngage, EngageButton = ButtonId.LeftShoulder };
        var processor = new GyroProcessor();

        var idle = processor.Process(rule, Motion(yaw: 1.0f));
        Assert.False(idle.Engaged);
        Assert.Equal(0f, idle.Horizontal, precision: 5);

        var held = processor.Process(rule, Motion(yaw: 1.0f, pressed: ButtonId.LeftShoulder));
        Assert.True(held.Engaged);
        Assert.NotEqual(0f, held.Horizontal);
    }

    [Fact]
    public void HoldToDisable_IsTheInverse()
    {
        var rule = Rule() with { EngageMode = GyroEngageMode.HoldToDisable, EngageButton = ButtonId.LeftShoulder };
        var processor = new GyroProcessor();

        Assert.True(processor.Process(rule, Motion(yaw: 1.0f)).Engaged);
        Assert.False(processor.Process(rule, Motion(yaw: 1.0f, pressed: ButtonId.LeftShoulder)).Engaged);
    }

    [Fact]
    public void Toggle_FlipsOnEachPressNotEachTick()
    {
        var rule = Rule() with { EngageMode = GyroEngageMode.Toggle, EngageButton = ButtonId.Back };
        var processor = new GyroProcessor();

        Assert.False(processor.Process(rule, Motion(yaw: 1f)).Engaged);

        // Press and HOLD across several ticks — must engage once, not oscillate.
        Assert.True(processor.Process(rule, Motion(yaw: 1f, pressed: ButtonId.Back)).Engaged);
        Assert.True(processor.Process(rule, Motion(yaw: 1f, pressed: ButtonId.Back)).Engaged);

        Assert.True(processor.Process(rule, Motion(yaw: 1f)).Engaged); // released, still on

        Assert.False(processor.Process(rule, Motion(yaw: 1f, pressed: ButtonId.Back)).Engaged); // second press turns it off
    }

    [Fact]
    public void StickGate_EngagesGyroFromARawStickNudge()
    {
        var rule = Rule() with
        {
            EngageMode = GyroEngageMode.HoldToEngage,
            EngageButton = ButtonId.LeftShoulder,
            StickGateEnabled = true,
            StickGateThreshold = 0.1f
        };
        var processor = new GyroProcessor();

        Assert.False(processor.Process(rule, Motion(yaw: 1f)).Engaged);

        // A nudge most games would swallow in their own deadzone still arms aiming.
        var nudged = processor.Process(rule, Motion(yaw: 1f, leftStick: new StickVector(0.15f, 0f)));
        Assert.True(nudged.Engaged);
    }

    [Fact]
    public void Smoothing_DampensSmallJitterButPassesFastFlicksThrough()
    {
        var rule = Rule() with
        {
            SmoothingLowerThreshold = 0.1f,
            SmoothingUpperThreshold = 0.5f,
            SmoothingWindowSamples = 4
        };

        // Alternating small jitter around zero: the smoothed average
        // pulls it toward zero, so output magnitude stays below input.
        var jitterProcessor = new GyroProcessor();
        _ = jitterProcessor.Process(rule, Motion(yaw: 0.05f));
        _ = jitterProcessor.Process(rule, Motion(yaw: -0.05f));
        _ = jitterProcessor.Process(rule, Motion(yaw: 0.05f));
        var jittered = jitterProcessor.Process(rule, Motion(yaw: -0.05f));
        Assert.True(MathF.Abs(jittered.Horizontal) < 0.05f);

        // A fast flick sits above the upper threshold and passes through undamped.
        var flickProcessor = new GyroProcessor();
        var flick = flickProcessor.Process(rule, Motion(yaw: 3.0f));
        Assert.Equal(3.0f, MathF.Abs(flick.Horizontal), precision: 3);
    }

    [Fact]
    public void ToStick_ClampsAtFullDeflection()
    {
        // Far beyond the reference rate — must saturate, not overflow past 1.
        var stick = GyroProcessor.ToStick(new GyroAimResult(true, 100f, -100f));

        Assert.Equal(1f, stick.X, precision: 4);
        Assert.Equal(-1f, stick.Y, precision: 4);
    }

    [Fact]
    public void ToMouseDelta_ScalesWithElapsedTime_AndFlipsYForScreenCoordinates()
    {
        var aim = new GyroAimResult(true, 1.0f, 1.0f);

        var (dxShort, _) = GyroProcessor.ToMouseDelta(aim, 0.01f);
        var (dxLong, dyLong) = GyroProcessor.ToMouseDelta(aim, 0.02f);

        // Angular velocity is a rate, so twice the elapsed time is twice the movement.
        Assert.Equal(dxShort * 2f, dxLong, precision: 3);
        // Aiming up (+vertical) must move the cursor UP, which is negative in screen coordinates.
        Assert.True(dyLong < 0f);
    }
}
