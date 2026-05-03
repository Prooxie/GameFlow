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

        var first = new ControllerSnapshot
        {
            RightStick = new StickVector(0.9f, 0f),
            Buttons = buttons
        };

        _ = pipeline.Process(first, DateTimeOffset.UtcNow);

        buttons[ButtonId.LeftShoulder] = true;

        var second = new ControllerSnapshot
        {
            RightStick = StickVector.Zero,
            Buttons = buttons
        };

        var result = pipeline.Process(second, DateTimeOffset.UtcNow.AddMilliseconds(16));

        Assert.Equal(new StickVector(0.9f, 0f), result.VirtualSnapshot.LeftStick);
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
