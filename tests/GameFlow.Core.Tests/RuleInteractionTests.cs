using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using GameFlow.Core.Pipeline;
using Xunit;

namespace GameFlow.Core.Tests;

/// <summary>
/// Regression tests for rules blocking or nullifying OTHER rules — the
/// "sometimes a rule just stops working" reports. Each test pins one way
/// it used to happen: one rule's suppression, block, or idle write wiping
/// another rule's output, or the physical input it should have left alone.
/// </summary>
public sealed class RuleInteractionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static ControllerSnapshot Frame(params ButtonId[] pressed)
    {
        var buttons = ButtonMask.Empty;
        foreach (var id in pressed)
        {
            buttons[id] = true;
        }
        return new ControllerSnapshot { Buttons = buttons };
    }

    private static ControllerFrameResult Run(ProfileDocument profile, ControllerSnapshot frame)
    {
        using var pipeline = new ControllerMappingPipeline(profile);
        return pipeline.Process(frame, T0);
    }

    // ── Shift layers override Base by input ──

    [Fact]
    public void LayerRemap_ReplacesTheBaseRemapOnTheSameButton_InsteadOfAddingToIt()
    {
        var profile = new ProfileDocument
        {
            ShiftLayers = [new ShiftLayer { Id = "aim", ActivationMode = ShiftLayerActivationMode.Hold, ActivatorButton = ButtonId.LeftShoulder }],
            Rules =
            [
                new ButtonRemapRule { Id = "base", SourceButton = ButtonId.South, TargetButton = ButtonId.West },
                new ButtonRemapRule { Id = "layer", LayerId = "aim", SourceButton = ButtonId.South, TargetButton = ButtonId.North }
            ]
        };
        using var pipeline = new ControllerMappingPipeline(profile);

        var onBase = pipeline.Process(Frame(ButtonId.South), T0).VirtualSnapshot;
        Assert.True(onBase.IsPressed(ButtonId.West));
        Assert.False(onBase.IsPressed(ButtonId.North));

        var inLayer = pipeline.Process(Frame(ButtonId.South, ButtonId.LeftShoulder), T0.AddMilliseconds(16)).VirtualSnapshot;
        Assert.True(inLayer.IsPressed(ButtonId.North));
        Assert.False(inLayer.IsPressed(ButtonId.West));
    }

    [Fact]
    public void LayerLeavesBaseRulesOnOtherInputsAlone_AndADisabledLayerRuleOverridesNothing()
    {
        var profile = new ProfileDocument
        {
            ShiftLayers = [new ShiftLayer { Id = "aim", ActivationMode = ShiftLayerActivationMode.Hold, ActivatorButton = ButtonId.LeftShoulder }],
            Rules =
            [
                new ButtonRemapRule { Id = "base-south", SourceButton = ButtonId.South, TargetButton = ButtonId.West },
                new ButtonRemapRule { Id = "base-east", SourceButton = ButtonId.East, TargetButton = ButtonId.North },
                new ButtonRemapRule { Id = "layer-east", LayerId = "aim", Enabled = false, SourceButton = ButtonId.East, TargetButton = ButtonId.Start }
            ]
        };

        var result = Run(profile, Frame(ButtonId.South, ButtonId.East, ButtonId.LeftShoulder)).VirtualSnapshot;

        Assert.True(result.IsPressed(ButtonId.West));
        Assert.True(result.IsPressed(ButtonId.North));
        Assert.False(result.IsPressed(ButtonId.Start));
    }

    // ── Suppression only ever hides the PHYSICAL press ──

    [Fact]
    public void TwoSuppressingRemaps_SwapButtons_WithoutWipingEachOther()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new ButtonRemapRule { Id = "a", SourceButton = ButtonId.South, TargetButton = ButtonId.East, SuppressSourceButton = true },
                new ButtonRemapRule { Id = "b", SourceButton = ButtonId.East, TargetButton = ButtonId.South, SuppressSourceButton = true }
            ]
        };

        var both = Run(profile, Frame(ButtonId.South, ButtonId.East)).VirtualSnapshot;
        Assert.True(both.IsPressed(ButtonId.South));
        Assert.True(both.IsPressed(ButtonId.East));

        var southOnly = Run(profile, Frame(ButtonId.South)).VirtualSnapshot;
        Assert.True(southOnly.IsPressed(ButtonId.East));
        Assert.False(southOnly.IsPressed(ButtonId.South));
    }

    [Fact]
    public void ARemapOntoItsOwnSource_WithSuppression_StillPresses()
    {
        var profile = new ProfileDocument
        {
            Rules = [new ButtonRemapRule { Id = "self", SourceButton = ButtonId.South, TargetButton = ButtonId.South, SuppressSourceButton = true }]
        };

        Assert.True(Run(profile, Frame(ButtonId.South)).VirtualSnapshot.IsPressed(ButtonId.South));
    }

    [Fact]
    public void MultiSourceSuppression_DoesNotEraseAButtonARemapProduced()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new ButtonRemapRule { Id = "remap", SourceButton = ButtonId.North, TargetButton = ButtonId.South },
                new MultiSourceMapRule
                {
                    Id = "row",
                    Sources = [new MapSource { Kind = MapSourceKind.Button, Button = ButtonId.South }],
                    TargetKind = MapTargetKind.Button,
                    TargetButton = ButtonId.West,
                    SuppressSources = true
                }
            ]
        };

        var result = Run(profile, Frame(ButtonId.North)).VirtualSnapshot;

        Assert.True(result.IsPressed(ButtonId.West));
        Assert.True(result.IsPressed(ButtonId.South));
    }

    [Fact]
    public void ComboSuppression_DoesNotEraseAButtonARemapProduced()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new ButtonRemapRule { Id = "remap", SourceButton = ButtonId.North, TargetButton = ButtonId.South },
                new ButtonComboRule
                {
                    Id = "combo",
                    SourceButton = ButtonId.South,
                    SuppressSourceButton = true,
                    Steps = [new ButtonComboStep { Button = ButtonId.East, HoldMs = 100 }]
                }
            ]
        };

        Assert.True(Run(profile, Frame(ButtonId.North)).VirtualSnapshot.IsPressed(ButtonId.South));
    }

    [Fact]
    public void ScriptSuppression_KeepsAPressTheScriptMakesOnItsOwnSource()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new ControlScriptRule
                {
                    Id = "script",
                    ControlKey = "South",
                    SuppressSourceInput = true,
                    ScriptCode = "function on_tick(ctx) ctx.press(\"South\") end"
                }
            ]
        };

        Assert.True(Run(profile, Frame()).VirtualSnapshot.IsPressed(ButtonId.South));
    }

    // ── Turbo on the button you are holding ──

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TurboOnItsOwnButton_Pulses(bool suppressSource)
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new ButtonAutofireRule
                {
                    Id = "turbo",
                    SourceButton = ButtonId.South,
                    TargetButton = ButtonId.South,
                    SuppressSourceButton = suppressSource,
                    Timing = new PulseTimingOptions { HoldMs = 60, ReleaseMs = 40 }
                }
            ]
        };
        using var pipeline = new ControllerMappingPipeline(profile);

        var pressed = pipeline.Process(Frame(ButtonId.South), T0).VirtualSnapshot;
        var released = pipeline.Process(Frame(ButtonId.South), T0.AddMilliseconds(70)).VirtualSnapshot;
        var pressedAgain = pipeline.Process(Frame(ButtonId.South), T0.AddMilliseconds(115)).VirtualSnapshot;

        Assert.True(pressed.IsPressed(ButtonId.South));
        Assert.False(released.IsPressed(ButtonId.South));
        Assert.True(pressedAgain.IsPressed(ButtonId.South));
    }

    // ── Block means "the source is blocked", for every rule type ──

    [Fact]
    public void AnAutofireBlock_DoesNotEraseARemapsOutputOnThatButton()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new ButtonRemapRule { Id = "remap", SourceButton = ButtonId.North, TargetButton = ButtonId.South },
                new ButtonAutofireRule { Id = "blocked", Mode = RuleMode.DoNothing, SourceButton = ButtonId.South, TargetButton = ButtonId.East }
            ]
        };

        var fromRemap = Run(profile, Frame(ButtonId.North)).VirtualSnapshot;
        Assert.True(fromRemap.IsPressed(ButtonId.South));

        var physical = Run(profile, Frame(ButtonId.South)).VirtualSnapshot;
        Assert.False(physical.IsPressed(ButtonId.South));
    }

    [Fact]
    public void BlockingStickAutofire_BlocksItsSourceStick_AndLeavesTheTargetAlone()
    {
        var profile = new ProfileDocument
        {
            Rules = [new StickAutofireRule { Id = "af", Mode = RuleMode.DoNothing, SourceStick = StickId.Right, TargetStick = StickId.Left }]
        };
        var frame = new ControllerSnapshot { LeftStick = new StickVector(0.6f, 0f), RightStick = new StickVector(0f, 0.7f) };

        var result = Run(profile, frame).VirtualSnapshot;

        Assert.Equal(new StickVector(0.6f, 0f), result.LeftStick);
        Assert.Equal(StickVector.Zero, result.RightStick);
    }

    [Fact]
    public void BlockingFreeze_BlocksItsActivationButton_AndLeavesTheTargetStickAlone()
    {
        var profile = new ProfileDocument
        {
            Rules = [new FreezeLastDirectionRule { Id = "freeze", Mode = RuleMode.DoNothing, ActivationButton = ButtonId.LeftShoulder, TargetStick = StickId.Left }]
        };
        var frame = Frame(ButtonId.LeftShoulder) with { LeftStick = new StickVector(0.6f, 0f) };

        var result = Run(profile, frame).VirtualSnapshot;

        Assert.Equal(new StickVector(0.6f, 0f), result.LeftStick);
        Assert.False(result.IsPressed(ButtonId.LeftShoulder));
    }

    // ── Idle rules no longer write over physical input ──

    [Fact]
    public void GyroOnAPadWithoutAGyro_LeavesThePhysicalStickWorking()
    {
        var profile = new ProfileDocument { Rules = [new GyroMapRule { Id = "gyro", TargetStick = StickId.Right }] };
        var frame = new ControllerSnapshot { RightStick = new StickVector(0.5f, -0.3f), HasGyro = false };

        Assert.Equal(new StickVector(0.5f, -0.3f), Run(profile, frame).VirtualSnapshot.RightStick);
    }

    [Fact]
    public void EngagedGyro_AddsToThePhysicalStick()
    {
        var rule = new GyroMapRule { Id = "gyro", TargetStick = StickId.Right, ReferenceFrame = GyroReferenceFrame.Local, SmoothingLowerThreshold = 0f, SmoothingUpperThreshold = 0f };
        var frame = new ControllerSnapshot { RightStick = new StickVector(0.5f, 0f), HasGyro = true, GyroYaw = 1.0f };
        var aim = GyroProcessor.ToStick(new GyroProcessor().Process(rule, frame));
        Assert.NotEqual(0f, aim.X);

        var result = Run(new ProfileDocument { Rules = [rule] }, frame).VirtualSnapshot;

        Assert.Equal(Math.Clamp(0.5f + aim.X, -1f, 1f), result.RightStick.X, precision: 4);
    }

    [Fact]
    public void TouchpadWithNoFingerDown_LeavesThePhysicalStickAndDpadWorking()
    {
        var profile = new ProfileDocument
        {
            Rules = [new TouchpadMapRule { Id = "pad", StickEnabled = true, TargetStick = StickId.Right, DpadEnabled = true }]
        };
        var frame = Frame(ButtonId.DpadUp) with { RightStick = new StickVector(0.4f, 0.4f) };

        var result = Run(profile, frame).VirtualSnapshot;

        Assert.Equal(new StickVector(0.4f, 0.4f), result.RightStick);
        Assert.True(result.IsPressed(ButtonId.DpadUp));
    }

    [Fact]
    public void TouchpadDpad_AddsToThePhysicalDpad_WhileTouching()
    {
        var profile = new ProfileDocument
        {
            Rules = [new TouchpadMapRule { Id = "pad", StickEnabled = false, DpadEnabled = true, DpadDeadzoneRadius = 0.02f, DpadEightWay = false }]
        };
        using var pipeline = new ControllerMappingPipeline(profile);

        _ = pipeline.Process(Frame(ButtonId.DpadUp).WithTouch(true, 0.5f, 0.5f), T0);
        var result = pipeline.Process(Frame(ButtonId.DpadUp).WithTouch(true, 0.7f, 0.5f), T0.AddMilliseconds(16)).VirtualSnapshot;

        Assert.True(result.IsPressed(ButtonId.DpadRight));
        Assert.True(result.IsPressed(ButtonId.DpadUp));
    }

    [Fact]
    public void UnarmedStickTrim_LeavesThePhysicalTriggerWorking_AndArmedNeverPullsItBelowThePhysical()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new StickTrimRule
                {
                    Id = "trim", ArmButton = ButtonId.LeftShoulder, ModulatorStick = StickId.Right,
                    TargetTrigger = TriggerId.Right, Deadzone = 0f, RampRatePerSecond = 0f, ResetOnRelease = true
                }
            ]
        };
        using var pipeline = new ControllerMappingPipeline(profile);

        var unarmed = pipeline.Process(Frame() with { RightTrigger = 0.8f }, T0).VirtualSnapshot;
        Assert.Equal(0.8f, unarmed.RightTrigger, precision: 4);

        var armedLight = pipeline.Process(Frame(ButtonId.LeftShoulder) with { RightTrigger = 0.8f, RightStick = new StickVector(0f, 0.3f) }, T0.AddMilliseconds(16)).VirtualSnapshot;
        Assert.Equal(0.8f, armedLight.RightTrigger, precision: 4);

        var armedHard = pipeline.Process(Frame(ButtonId.LeftShoulder) with { RightTrigger = 0.1f, RightStick = new StickVector(0f, 0.9f) }, T0.AddMilliseconds(32)).VirtualSnapshot;
        Assert.Equal(0.9f, armedHard.RightTrigger, precision: 4);
    }

    // ── Per-rule state that used to be shared ──

    [Fact]
    public void TwoFreezeRulesOnTheSameButton_BothCaptureOnThePress()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new FreezeLastDirectionRule { Id = "a", ActivationButton = ButtonId.LeftShoulder, CaptureStick = StickId.Right, TargetStick = StickId.Left, BlendMode = StickBlendMode.Replace, PulseEnabled = false },
                new FreezeLastDirectionRule { Id = "b", ActivationButton = ButtonId.LeftShoulder, CaptureStick = StickId.Left, TargetStick = StickId.Right, BlendMode = StickBlendMode.Replace, PulseEnabled = false }
            ]
        };
        using var pipeline = new ControllerMappingPipeline(profile);

        var frame = Frame(ButtonId.LeftShoulder) with { LeftStick = new StickVector(0.2f, 0f), RightStick = new StickVector(0f, 0.9f) };
        var result = pipeline.Process(frame, T0).VirtualSnapshot;

        Assert.Equal(new StickVector(0f, 0.9f), result.LeftStick);
        Assert.Equal(new StickVector(0.2f, 0f), result.RightStick);
    }

    // ── Rule toggles ──

    [Fact]
    public void RuleToggle_CanSwitchOnARuleSavedDisabled_AndOffAgain()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new ButtonRemapRule { Id = "remap", Enabled = false, SourceButton = ButtonId.South, TargetButton = ButtonId.North },
                new RuleToggleRule { Id = "toggle", SourceButton = ButtonId.Touchpad, TargetRuleIds = ["remap"] }
            ]
        };
        using var pipeline = new ControllerMappingPipeline(profile);

        Assert.False(pipeline.Process(Frame(ButtonId.South), T0).VirtualSnapshot.IsPressed(ButtonId.North));

        _ = pipeline.Process(Frame(ButtonId.Touchpad), T0.AddMilliseconds(16));
        Assert.True(pipeline.Process(Frame(ButtonId.South), T0.AddMilliseconds(32)).VirtualSnapshot.IsPressed(ButtonId.North));

        _ = pipeline.Process(Frame(ButtonId.Touchpad), T0.AddMilliseconds(48));
        Assert.False(pipeline.Process(Frame(ButtonId.South), T0.AddMilliseconds(64)).VirtualSnapshot.IsPressed(ButtonId.North));
    }

    [Fact]
    public void RuleToggleOnALayer_OnlyFiresWhileThatLayerIsActive()
    {
        var profile = new ProfileDocument
        {
            ShiftLayers = [new ShiftLayer { Id = "menu", ActivationMode = ShiftLayerActivationMode.Hold, ActivatorButton = ButtonId.LeftShoulder }],
            Rules =
            [
                new ButtonRemapRule { Id = "remap", Enabled = false, SourceButton = ButtonId.South, TargetButton = ButtonId.North },
                new RuleToggleRule { Id = "toggle", LayerId = "menu", SourceButton = ButtonId.Touchpad, TargetRuleIds = ["remap"] }
            ]
        };
        using var pipeline = new ControllerMappingPipeline(profile);

        _ = pipeline.Process(Frame(ButtonId.Touchpad), T0);
        _ = pipeline.Process(Frame(), T0.AddMilliseconds(16));
        Assert.False(pipeline.Process(Frame(ButtonId.South), T0.AddMilliseconds(32)).VirtualSnapshot.IsPressed(ButtonId.North));

        _ = pipeline.Process(Frame(ButtonId.LeftShoulder, ButtonId.Touchpad), T0.AddMilliseconds(48));
        Assert.True(pipeline.Process(Frame(ButtonId.South), T0.AddMilliseconds(64)).VirtualSnapshot.IsPressed(ButtonId.North));
    }

    // ── Remaps read SOCD-cleaned input ──

    [Fact]
    public void Remap_ReadsTheSocdCleanedInput()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new SocdCleanRule { Id = "socd", NegativeButton = ButtonId.DpadLeft, PositiveButton = ButtonId.DpadRight, SocdMode = SocdMode.LastWins },
                new ButtonRemapRule { Id = "remap", SourceButton = ButtonId.DpadLeft, TargetButton = ButtonId.West }
            ]
        };
        using var pipeline = new ControllerMappingPipeline(profile);

        Assert.True(pipeline.Process(Frame(ButtonId.DpadLeft), T0).VirtualSnapshot.IsPressed(ButtonId.West));

        // Right joins later and wins the pair: Left is cleaned away, so the
        // remap that listens to Left must stop pressing West too.
        var both = pipeline.Process(Frame(ButtonId.DpadLeft, ButtonId.DpadRight), T0.AddMilliseconds(16)).VirtualSnapshot;
        Assert.False(both.IsPressed(ButtonId.West));
        Assert.True(both.IsPressed(ButtonId.DpadRight));
    }

    // ── Sticks: a rule's own suppression no longer wipes its own output ──

    [Fact]
    public void StickAutofireOntoItsOwnStick_WithSuppression_OutputsThePulse()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new StickAutofireRule
                {
                    Id = "af", SourceStick = StickId.Right, TargetStick = StickId.Right, BlendMode = StickBlendMode.Additive,
                    SuppressSourceStick = true, ActivationDeadzone = 0f, ActivationFullAt = 1f,
                    Timing = new PulseTimingOptions { HoldMs = 60, ReleaseMs = 40 }
                }
            ]
        };

        var result = Run(profile, new ControllerSnapshot { RightStick = new StickVector(0.5f, 0f) }).VirtualSnapshot;

        Assert.Equal(0.5f, result.RightStick.X, precision: 4);
    }

    [Fact]
    public void FreezeIntoTheStickItCaptures_WithCaptureSuppression_KeepsTheFrozenValue()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new FreezeLastDirectionRule
                {
                    Id = "freeze", ActivationButton = ButtonId.LeftShoulder, CaptureStick = StickId.Left, TargetStick = StickId.Left,
                    BlendMode = StickBlendMode.Additive, SuppressCaptureStick = true, PulseEnabled = false
                }
            ]
        };

        var result = Run(profile, Frame(ButtonId.LeftShoulder) with { LeftStick = new StickVector(0.7f, 0f) }).VirtualSnapshot;

        Assert.Equal(new StickVector(0.7f, 0f), result.LeftStick);
    }

    [Fact]
    public void ThresholdSuppression_CentresTheStick_ButStillFeedsAutofire()
    {
        var profile = new ProfileDocument
        {
            Rules =
            [
                new StickThresholdRule { Id = "shape", TargetStick = StickId.Right, Deadzone = 0f, FullAt = 1f, SuppressSourceStick = true },
                new StickAutofireRule
                {
                    Id = "af", SourceStick = StickId.Right, TargetStick = StickId.Left, BlendMode = StickBlendMode.Replace,
                    ActivationDeadzone = 0f, ActivationFullAt = 1f, Timing = new PulseTimingOptions { HoldMs = 60, ReleaseMs = 40 }
                }
            ]
        };

        var result = Run(profile, new ControllerSnapshot { RightStick = new StickVector(0.6f, 0f) }).VirtualSnapshot;

        Assert.Equal(StickVector.Zero, result.RightStick);
        Assert.Equal(0.6f, result.LeftStick.X, precision: 4);
    }

    // ── Layer activator suppression (opt-in) ──

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void LayerActivator_ReachesTheGameUnlessTheLayerSuppressesIt(bool suppress, bool expectedPressed)
    {
        var profile = new ProfileDocument
        {
            ShiftLayers =
            [
                new ShiftLayer { Id = "aim", ActivationMode = ShiftLayerActivationMode.Hold, ActivatorButton = ButtonId.LeftShoulder, SuppressActivatorButton = suppress }
            ]
        };

        var result = Run(profile, Frame(ButtonId.LeftShoulder));

        Assert.Equal("aim", result.ActiveLayerId);
        Assert.Equal(expectedPressed, result.VirtualSnapshot.IsPressed(ButtonId.LeftShoulder));
    }
}
