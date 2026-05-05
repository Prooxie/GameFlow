using Autofire.Core.Enums;
using Autofire.Core.Models;
using Autofire.Core.Models.Rules;
using Autofire.Core.Pipeline;
using Xunit;

namespace Autofire.Core.Tests;

public sealed class ControllerMappingPipelineTests
{
    [Fact]
    public void Process_ShouldFreezeLastDirectionWhileButtonIsHeld()
    {
        // Issue #12: The freeze latch now captures the stick vector at the RISING EDGE
        // of the activation button — whatever the stick is doing the instant you press.
        // This test verifies that the captured value (0.9, 0) is frozen onto the left stick
        // while LeftShoulder is held, even though the right stick has that same value
        // at the moment of the press (this is the intended workflow: hold stick, press button).

        var profile = new ProfileDocument
        {
            Rules =
            [
                new FreezeLastDirectionRule
                {
                    Id = "freeze",
                    Name = "Freeze",
                    ActivationButton = ButtonId.LeftShoulder,
                    CaptureStick = StickId.Right,
                    TargetStick = StickId.Left,
                    BlendMode = StickBlendMode.Replace,
                    PulseEnabled = false
                }
            ]
        };

        var pipeline = new ControllerMappingPipeline(profile);
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());

        // Frame 1: stick is active, button not pressed yet
        var first = new ControllerSnapshot
        {
            RightStick = new StickVector(0.9f, 0f),
            Buttons = buttons
        };

        _ = pipeline.Process(first, DateTimeOffset.UtcNow);

        // Frame 2: button pressed (RISING EDGE) while stick is still at (0.9, 0).
        // The latch captures whatever the stick is RIGHT NOW — (0.9, 0).
        buttons[ButtonId.LeftShoulder] = true;

        var second = new ControllerSnapshot
        {
            RightStick = new StickVector(0.9f, 0f),   // stick still at value during press
            Buttons = buttons
        };

        var result = pipeline.Process(second, DateTimeOffset.UtcNow.AddMilliseconds(16));

        Assert.Equal(new StickVector(0.9f, 0f), result.VirtualSnapshot.LeftStick);
    }

    [Fact]
    public void Process_ShouldFreezePreviousStickValueWhenButtonPressedAfterStickReleased()
    {
        // Demonstrates that if the stick is released (zero) AT the moment of the button press,
        // the frozen value is Zero — this is the correct rising-edge capture behavior.
        var profile = new ProfileDocument
        {
            Rules =
            [
                new FreezeLastDirectionRule
                {
                    Id = "freeze",
                    Name = "Freeze",
                    ActivationButton = ButtonId.LeftShoulder,
                    CaptureStick = StickId.Right,
                    TargetStick = StickId.Left,
                    BlendMode = StickBlendMode.Replace,
                    PulseEnabled = false
                }
            ]
        };

        var pipeline = new ControllerMappingPipeline(profile);
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());

        _ = pipeline.Process(new ControllerSnapshot { RightStick = new StickVector(0.9f, 0f), Buttons = buttons },
            DateTimeOffset.UtcNow);

        buttons[ButtonId.LeftShoulder] = true;

        // Rising edge fires, but stick is now zero — latch captures zero
        var result = pipeline.Process(
            new ControllerSnapshot { RightStick = StickVector.Zero, Buttons = buttons },
            DateTimeOffset.UtcNow.AddMilliseconds(16));

        Assert.Equal(StickVector.Zero, result.VirtualSnapshot.LeftStick);
    }

    [Fact]
    public void Process_ShouldTurboButtonWhenSourceIsHeld()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new ButtonAutofireRule
                {
                    Id = "turbo",
                    Name = "Turbo",
                    SourceButton = ButtonId.RightShoulder,
                    TargetButton = ButtonId.South,
                    Timing = new PulseTimingOptions
                    {
                        HoldMs = 60,
                        ReleaseMs = 40
                    }
                }
            ]
        };

        var pipeline = new ControllerMappingPipeline(profile);
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        buttons[ButtonId.RightShoulder] = true;

        var frame = new ControllerSnapshot { Buttons = buttons };

        var first = pipeline.Process(frame, DateTimeOffset.UtcNow);
        var second = pipeline.Process(frame, DateTimeOffset.UtcNow.AddMilliseconds(80));

        Assert.True(first.VirtualSnapshot.IsPressed(ButtonId.South));
        Assert.False(second.VirtualSnapshot.IsPressed(ButtonId.South));
    }
}
