using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using GameFlow.Core.Pipeline;
using Xunit;
// Same collision as ControllerMappingPipeline.cs: this file imports both
// GameFlow.Core.Enums and GameFlow.Core.Models.Rules, and a leftover
// GameFlow.Core.Enums.CombineMode (from the now-removed parallel
// implementation) still exists on disk. This alias resolves every bare
// CombineMode reference below to the canonical one in Models.Rules.
using CombineMode = GameFlow.Core.Models.Rules.CombineMode;

namespace GameFlow.Core.Tests;

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

    [Fact]
    public void Process_ShouldExecuteControlScriptRule_PressButton()
    {
        // ControlScriptRule.cs's own doc comment calls runtime execution
        // "intentionally handled by higher layers" -- ControllerMappingPipeline
        // is that layer. This verifies the wiring end-to-end: a script that
        // calls ctx.press("South") makes South true in the virtual output,
        // with no physical South press anywhere in the input.
        var profile = new ProfileDocument
        {
            Rules =
            [
                new ControlScriptRule
                {
                    Id = "script-press",
                    Name = "Always press South",
                    ControlKey = "South",
                    ScriptCode = "function on_tick(ctx) ctx.press(\"South\") end"
                }
            ]
        };

        using var pipeline = new ControllerMappingPipeline(profile);
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        var frame = new ControllerSnapshot { Buttons = buttons };

        var result = pipeline.Process(frame, DateTimeOffset.UtcNow);

        Assert.True(result.VirtualSnapshot.IsPressed(ButtonId.South));
        Assert.False(result.PhysicalSnapshot.IsPressed(ButtonId.South));
    }

    [Fact]
    public void Process_ShouldExecuteControlScriptRule_SetTriggerFromStick()
    {
        // Scripts are the first rule type able to WRITE a trigger value —
        // before this pass existed, LeftTrigger/RightTrigger passed through
        // Process() completely unmodified. Verifies ctx.set_rt(...) actually
        // reaches the final virtual snapshot via the new WithTriggers() call.
        var profile = new ProfileDocument
        {
            Rules =
            [
                new ControlScriptRule
                {
                    Id = "script-trigger",
                    Name = "Right stick Y drives right trigger",
                    ControlKey = "RightTrigger.Analog",
                    ScriptCode = "function on_tick(ctx) ctx.set_rt(ctx.right.y) end"
                }
            ]
        };

        using var pipeline = new ControllerMappingPipeline(profile);
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        var frame = new ControllerSnapshot { RightStick = new StickVector(0f, 0.75f), Buttons = buttons };

        var result = pipeline.Process(frame, DateTimeOffset.UtcNow);

        Assert.Equal(0.75f, result.VirtualSnapshot.RightTrigger, precision: 3);
    }

    [Fact]
    public void Process_ShouldSuppressSourceButton_WhenControlKeyNamesAButtonAndSuppressIsSet()
    {
        // SuppressSourceInput's one unambiguous case: ControlKey names a
        // button outright. The physical South press should not leak into
        // the virtual output once the script (which does nothing with
        // South here) has run and suppression is applied.
        var profile = new ProfileDocument
        {
            Rules =
            [
                new ControlScriptRule
                {
                    Id = "script-suppress",
                    Name = "Suppress South",
                    ControlKey = "South",
                    SuppressSourceInput = true,
                    ScriptCode = "function on_tick(ctx) end"
                }
            ]
        };

        using var pipeline = new ControllerMappingPipeline(profile);
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        buttons[ButtonId.South] = true;
        var frame = new ControllerSnapshot { Buttons = buttons };

        var result = pipeline.Process(frame, DateTimeOffset.UtcNow);

        Assert.True(result.PhysicalSnapshot.IsPressed(ButtonId.South));
        Assert.False(result.VirtualSnapshot.IsPressed(ButtonId.South));
    }

    private static ControllerSnapshot Frame(ButtonId? pressed = null)
    {
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        if (pressed is { } id)
        {
            buttons[id] = true;
        }
        return new ControllerSnapshot { Buttons = buttons };
    }

    [Fact]
    public void Process_HoldLayer_ActivatesOnlyWhileButtonIsHeld()
    {
        var layer = new ShiftLayer
        {
            Id = "aim",
            Name = "Aim",
            ActivationMode = ShiftLayerActivationMode.Hold,
            ActivatorButton = ButtonId.LeftShoulder
        };
        var profile = new ProfileDocument
        {
            ShiftLayers = [layer],
            Rules =
            [
                new ButtonRemapRule { Id = "base", SourceButton = ButtonId.South, TargetButton = ButtonId.West },
                new ButtonRemapRule { Id = "layer", LayerId = "aim", SourceButton = ButtonId.South, TargetButton = ButtonId.North }
            ]
        };

        using var pipeline = new ControllerMappingPipeline(profile);
        var now = DateTimeOffset.UtcNow;

        var baseResult = pipeline.Process(Frame(ButtonId.South), now);
        Assert.True(baseResult.VirtualSnapshot.IsPressed(ButtonId.West));
        Assert.False(baseResult.VirtualSnapshot.IsPressed(ButtonId.North));
        Assert.Equal(string.Empty, baseResult.ActiveLayerId);

        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        buttons[ButtonId.LeftShoulder] = true;
        buttons[ButtonId.South] = true;
        var heldFrame = new ControllerSnapshot { Buttons = buttons };

        var heldResult = pipeline.Process(heldFrame, now.AddMilliseconds(16));
        Assert.Equal("aim", heldResult.ActiveLayerId);
        Assert.True(heldResult.VirtualSnapshot.IsPressed(ButtonId.North));

        var releasedResult = pipeline.Process(Frame(ButtonId.South), now.AddMilliseconds(32));
        Assert.Equal(string.Empty, releasedResult.ActiveLayerId);
        Assert.True(releasedResult.VirtualSnapshot.IsPressed(ButtonId.West));
    }

    [Fact]
    public void Process_ToggleLayer_QuickTapPassesThroughButHoldFlips()
    {
        var layer = new ShiftLayer
        {
            Id = "menu",
            ActivationMode = ShiftLayerActivationMode.Toggle,
            ActivatorButton = ButtonId.Back,
            HoldToFireMs = 300
        };
        var profile = new ProfileDocument { ShiftLayers = [layer] };
        using var pipeline = new ControllerMappingPipeline(profile);
        var now = DateTimeOffset.UtcNow;

        // Quick tap (< 300 ms hold gate): press then release well under the threshold.
        _ = pipeline.Process(Frame(ButtonId.Back), now);
        var afterQuickTap = pipeline.Process(Frame(), now.AddMilliseconds(50));
        Assert.Equal(string.Empty, afterQuickTap.ActiveLayerId);

        // Genuine hold (>= 300 ms): press, then release after the threshold.
        _ = pipeline.Process(Frame(ButtonId.Back), now.AddSeconds(1));
        var afterHold = pipeline.Process(Frame(), now.AddSeconds(1).AddMilliseconds(350));
        Assert.Equal("menu", afterHold.ActiveLayerId);

        // Toggle off: a second qualifying hold flips it back.
        _ = pipeline.Process(Frame(ButtonId.Back), now.AddSeconds(2));
        var afterSecondHold = pipeline.Process(Frame(), now.AddSeconds(2).AddMilliseconds(350));
        Assert.Equal(string.Empty, afterSecondHold.ActiveLayerId);
    }

    [Fact]
    public void Process_ToggleLayer_AutoCancelsAfterIdleTimeout()
    {
        var layer = new ShiftLayer
        {
            Id = "menu",
            ActivationMode = ShiftLayerActivationMode.Toggle,
            ActivatorButton = ButtonId.Back,
            AutoCancelMs = 200
        };
        var profile = new ProfileDocument { ShiftLayers = [layer] };
        using var pipeline = new ControllerMappingPipeline(profile);
        var now = DateTimeOffset.UtcNow;

        _ = pipeline.Process(Frame(ButtonId.Back), now);
        var engaged = pipeline.Process(Frame(), now.AddMilliseconds(10));
        Assert.Equal("menu", engaged.ActiveLayerId);

        // Nothing happens for longer than AutoCancelMs — should self-revert.
        var idled = pipeline.Process(Frame(), now.AddMilliseconds(300));
        Assert.Equal(string.Empty, idled.ActiveLayerId);
    }

    [Fact]
    public void Process_LatchLayers_SwitchDirectlyBetweenEachOtherWithoutTouchingBase()
    {
        var layerA = new ShiftLayer { Id = "a", ActivationMode = ShiftLayerActivationMode.Latch, ActivatorButton = ButtonId.DpadUp };
        var layerB = new ShiftLayer { Id = "b", ActivationMode = ShiftLayerActivationMode.Latch, ActivatorButton = ButtonId.DpadDown };
        var profile = new ProfileDocument { ShiftLayers = [layerA, layerB] };
        using var pipeline = new ControllerMappingPipeline(profile);
        var now = DateTimeOffset.UtcNow;

        var afterA = pipeline.Process(Frame(ButtonId.DpadUp), now);
        Assert.Equal("a", afterA.ActiveLayerId);

        var released = pipeline.Process(Frame(), now.AddMilliseconds(16));
        Assert.Equal("a", released.ActiveLayerId); // Latch holds after release, unlike Hold

        var afterB = pipeline.Process(Frame(ButtonId.DpadDown), now.AddMilliseconds(32));
        Assert.Equal("b", afterB.ActiveLayerId); // direct switch, no detour through Base

        var afterBReleased = pipeline.Process(Frame(), now.AddMilliseconds(48));
        var afterBAgain = pipeline.Process(Frame(ButtonId.DpadDown), now.AddMilliseconds(64));
        Assert.Equal(string.Empty, afterBAgain.ActiveLayerId); // pressing its OWN button again returns to Base
    }

    [Fact]
    public void Process_CycleLayer_StepsForwardAndBackwardThroughItsQueue()
    {
        var stepOne = new ShiftLayer { Id = "one", ActivationMode = ShiftLayerActivationMode.NoButton };
        var stepTwo = new ShiftLayer { Id = "two", ActivationMode = ShiftLayerActivationMode.NoButton };
        var cycle = new ShiftLayer
        {
            Id = "cycle",
            ActivationMode = ShiftLayerActivationMode.Cycle,
            ActivatorButton = ButtonId.RightShoulder,
            CyclePreviousButton = ButtonId.LeftShoulder,
            CycleIncludeBase = true,
            CycleWrapAround = true,
            CycleLayerIds = ["one", "two"]
        };
        var profile = new ProfileDocument { ShiftLayers = [stepOne, stepTwo, cycle] };
        using var pipeline = new ControllerMappingPipeline(profile);
        var now = DateTimeOffset.UtcNow;
        var t = 0;
        DateTimeOffset Next() => now.AddMilliseconds(t += 16);

        // Stops, with CycleIncludeBase: [Base, "one", "two"].
        var first = pipeline.Process(Frame(ButtonId.RightShoulder), Next());
        Assert.Equal("one", first.ActiveLayerId);

        _ = pipeline.Process(Frame(), Next()); // release
        var second = pipeline.Process(Frame(ButtonId.RightShoulder), Next());
        Assert.Equal("two", second.ActiveLayerId);

        _ = pipeline.Process(Frame(), Next());
        var wrapped = pipeline.Process(Frame(ButtonId.RightShoulder), Next());
        Assert.Equal(string.Empty, wrapped.ActiveLayerId); // wraps past "two" back to Base

        _ = pipeline.Process(Frame(), Next());
        var backward = pipeline.Process(Frame(ButtonId.LeftShoulder), Next());
        Assert.Equal("two", backward.ActiveLayerId); // Previous wraps backward from Base to the last stop
    }

    private static ControllerSnapshot FrameWithButtons(params ButtonId[] pressed)
    {
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        foreach (var id in pressed)
        {
            buttons[id] = true;
        }
        return new ControllerSnapshot { Buttons = buttons };
    }

    [Fact]
    public void Process_Socd_LastWins_SwitchesToWhicheverWasPressedMostRecently()
    {
        var profile = new ProfileDocument
        {
            Rules = [new SocdCleanRule { Id = "socd", NegativeButton = ButtonId.DpadLeft, PositiveButton = ButtonId.DpadRight, SocdMode = SocdMode.LastWins }]
        };
        using var pipeline = new ControllerMappingPipeline(profile);
        var now = DateTimeOffset.UtcNow;

        // Hold Left alone first.
        var leftOnly = pipeline.Process(FrameWithButtons(ButtonId.DpadLeft), now);
        Assert.True(leftOnly.VirtualSnapshot.IsPressed(ButtonId.DpadLeft));

        // Right joins while Left is still held — Right is more recent, so Right wins (Snap Tap).
        var bothNow = pipeline.Process(FrameWithButtons(ButtonId.DpadLeft, ButtonId.DpadRight), now.AddMilliseconds(16));
        Assert.False(bothNow.VirtualSnapshot.IsPressed(ButtonId.DpadLeft));
        Assert.True(bothNow.VirtualSnapshot.IsPressed(ButtonId.DpadRight));

        // Release Right — Left, still physically held, immediately regains control.
        var rightReleased = pipeline.Process(FrameWithButtons(ButtonId.DpadLeft), now.AddMilliseconds(32));
        Assert.True(rightReleased.VirtualSnapshot.IsPressed(ButtonId.DpadLeft));
    }

    [Fact]
    public void Process_Socd_FirstWins_HoldsUntilTheFirstOneReleases()
    {
        var profile = new ProfileDocument
        {
            Rules = [new SocdCleanRule { Id = "socd", NegativeButton = ButtonId.DpadLeft, PositiveButton = ButtonId.DpadRight, SocdMode = SocdMode.FirstWins }]
        };
        using var pipeline = new ControllerMappingPipeline(profile);
        var now = DateTimeOffset.UtcNow;

        _ = pipeline.Process(FrameWithButtons(ButtonId.DpadLeft), now);
        var bothNow = pipeline.Process(FrameWithButtons(ButtonId.DpadLeft, ButtonId.DpadRight), now.AddMilliseconds(16));

        // Left was first and stays the winner even though Right is also held.
        Assert.True(bothNow.VirtualSnapshot.IsPressed(ButtonId.DpadLeft));
        Assert.False(bothNow.VirtualSnapshot.IsPressed(ButtonId.DpadRight));
    }

    [Fact]
    public void Process_Socd_Neutral_SuppressesBothWhileOverlapping()
    {
        var profile = new ProfileDocument
        {
            Rules = [new SocdCleanRule { Id = "socd", NegativeButton = ButtonId.DpadLeft, PositiveButton = ButtonId.DpadRight, SocdMode = SocdMode.Neutral }]
        };
        using var pipeline = new ControllerMappingPipeline(profile);
        var now = DateTimeOffset.UtcNow;

        var bothNow = pipeline.Process(FrameWithButtons(ButtonId.DpadLeft, ButtonId.DpadRight), now);
        Assert.False(bothNow.VirtualSnapshot.IsPressed(ButtonId.DpadLeft));
        Assert.False(bothNow.VirtualSnapshot.IsPressed(ButtonId.DpadRight));
    }

    [Fact]
    public void Process_StickTrim_RampsTowardStickMagnitudeWhileArmed()
    {
        var rule = new StickTrimRule
        {
            Id = "trim",
            ArmButton = ButtonId.LeftShoulder,
            ModulatorStick = StickId.Right,
            TargetTrigger = TriggerId.Right,
            Deadzone = 0f,
            RampRatePerSecond = 2f, // 0 -> 1 over 500ms
            ResetOnRelease = true
        };
        var profile = new ProfileDocument { Rules = [rule] };
        using var pipeline = new ControllerMappingPipeline(profile);
        var now = DateTimeOffset.UtcNow;

        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        buttons[ButtonId.LeftShoulder] = true;
        var armedFullStick = new ControllerSnapshot { Buttons = buttons, RightStick = new StickVector(0f, 1f) };

        // First tick establishes the dt baseline (no elapsed time yet to ramp over).
        _ = pipeline.Process(armedFullStick, now);

        // 250ms later, at a 2.0/s ramp rate, should be roughly halfway (0.5) toward 1.0.
        var midRamp = pipeline.Process(armedFullStick, now.AddMilliseconds(250));
        Assert.InRange(midRamp.VirtualSnapshot.RightTrigger, 0.35f, 0.65f);

        // Fully caught up after another full second.
        var fullyRamped = pipeline.Process(armedFullStick, now.AddMilliseconds(1250));
        Assert.True(fullyRamped.VirtualSnapshot.RightTrigger > 0.95f);
    }

    [Fact]
    public void Process_StickTrim_ResetOnReleaseSnapsToZero_WhenTrue()
    {
        var rule = new StickTrimRule
        {
            Id = "trim",
            ArmButton = ButtonId.LeftShoulder,
            ModulatorStick = StickId.Right,
            TargetTrigger = TriggerId.Right,
            Deadzone = 0f,
            RampRatePerSecond = 100f, // fast enough to fully saturate within the test's timings
            ResetOnRelease = true
        };
        var profile = new ProfileDocument { Rules = [rule] };
        using var pipeline = new ControllerMappingPipeline(profile);
        var now = DateTimeOffset.UtcNow;

        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        buttons[ButtonId.LeftShoulder] = true;
        var armed = new ControllerSnapshot { Buttons = buttons, RightStick = new StickVector(0f, 1f) };

        _ = pipeline.Process(armed, now);
        var pressed = pipeline.Process(armed, now.AddMilliseconds(100));
        Assert.True(pressed.VirtualSnapshot.RightTrigger > 0.9f);

        var released = pipeline.Process(Frame(), now.AddMilliseconds(116));
        Assert.Equal(0f, released.VirtualSnapshot.RightTrigger, precision: 3);
    }

    [Fact]
    public void Process_StickTrim_FreezesLastValue_WhenResetOnReleaseIsFalse()
    {
        var rule = new StickTrimRule
        {
            Id = "trim",
            ArmButton = ButtonId.LeftShoulder,
            ModulatorStick = StickId.Right,
            TargetTrigger = TriggerId.Right,
            Deadzone = 0f,
            RampRatePerSecond = 100f,
            ResetOnRelease = false
        };
        var profile = new ProfileDocument { Rules = [rule] };
        using var pipeline = new ControllerMappingPipeline(profile);
        var now = DateTimeOffset.UtcNow;

        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        buttons[ButtonId.LeftShoulder] = true;
        var armed = new ControllerSnapshot { Buttons = buttons, RightStick = new StickVector(0f, 1f) };

        _ = pipeline.Process(armed, now);
        var pressed = pipeline.Process(armed, now.AddMilliseconds(100));
        Assert.True(pressed.VirtualSnapshot.RightTrigger > 0.9f);

        var released = pipeline.Process(Frame(), now.AddMilliseconds(116));
        Assert.True(released.VirtualSnapshot.RightTrigger > 0.9f); // frozen, not snapped to zero
    }

    [Fact]
    public void Process_MultiSource_Maximum_EitherButtonDrivesTarget()
    {
        var rule = new MultiSourceMapRule
        {
            Id = "row",
            Sources =
            [
                new MapSource { Kind = MapSourceKind.Button, Button = ButtonId.South },
                new MapSource { Kind = MapSourceKind.Button, Button = ButtonId.East }
            ],
            CombineMode = CombineMode.Maximum,
            TargetKind = MapTargetKind.Button,
            TargetButton = ButtonId.North
        };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        Assert.True(pipeline.Process(FrameWithButtons(ButtonId.East), now).VirtualSnapshot.IsPressed(ButtonId.North));
        Assert.False(pipeline.Process(FrameWithButtons(), now.AddMilliseconds(16)).VirtualSnapshot.IsPressed(ButtonId.North));
    }

    [Fact]
    public void Process_MultiSource_SumOfTriggers_ClampsOnTriggerTarget()
    {
        var rule = new MultiSourceMapRule
        {
            Id = "row",
            Sources =
            [
                new MapSource { Kind = MapSourceKind.Trigger, Trigger = TriggerId.Left },
                new MapSource { Kind = MapSourceKind.Trigger, Trigger = TriggerId.Right }
            ],
            CombineMode = CombineMode.Sum,
            TargetKind = MapTargetKind.Trigger,
            TargetTrigger = TriggerId.Right
        };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });

        var frame = new ControllerSnapshot
        {
            Buttons = ButtonState.CreateEmptyMap(),
            LeftTrigger = 0.7f,
            RightTrigger = 0.7f
        };
        var result = pipeline.Process(frame, DateTimeOffset.UtcNow);
        Assert.Equal(1f, result.VirtualSnapshot.RightTrigger, precision: 3); // 1.4 clamped
    }

    [Fact]
    public void Process_MultiSource_MultiplyGatesAnalogThroughButton()
    {
        var rule = new MultiSourceMapRule
        {
            Id = "row",
            Sources =
            [
                new MapSource { Kind = MapSourceKind.Button, Button = ButtonId.LeftShoulder },
                new MapSource { Kind = MapSourceKind.Trigger, Trigger = TriggerId.Right }
            ],
            CombineMode = CombineMode.Multiply,
            TargetKind = MapTargetKind.Trigger,
            TargetTrigger = TriggerId.Left
        };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        var gateOpen = new ControllerSnapshot
        {
            Buttons = FrameWithButtons(ButtonId.LeftShoulder).Buttons,
            RightTrigger = 0.7f
        };
        Assert.Equal(0.7f, pipeline.Process(gateOpen, now).VirtualSnapshot.LeftTrigger, precision: 3);

        var gateClosed = new ControllerSnapshot
        {
            Buttons = ButtonState.CreateEmptyMap(),
            RightTrigger = 0.7f
        };
        Assert.Equal(0f, pipeline.Process(gateClosed, now.AddMilliseconds(16)).VirtualSnapshot.LeftTrigger, precision: 3);
    }

    [Fact]
    public void Process_MultiSource_FirstActive_PrefersEarlierSourcesInOrder()
    {
        var rule = new MultiSourceMapRule
        {
            Id = "row",
            Sources =
            [
                new MapSource { Kind = MapSourceKind.Trigger, Trigger = TriggerId.Left },
                new MapSource { Kind = MapSourceKind.Trigger, Trigger = TriggerId.Right }
            ],
            CombineMode = CombineMode.FirstActive,
            TargetKind = MapTargetKind.Trigger,
            TargetTrigger = TriggerId.Left
        };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });

        var bothActive = new ControllerSnapshot
        {
            Buttons = ButtonState.CreateEmptyMap(),
            LeftTrigger = 0.3f,
            RightTrigger = 0.9f
        };
        // Left is first in the list and active — it wins outright, even though Right is stronger.
        Assert.Equal(0.3f, pipeline.Process(bothActive, DateTimeOffset.UtcNow).VirtualSnapshot.LeftTrigger, precision: 3);
    }

    [Fact]
    public void Process_MultiSource_Formula_TwoButtonsBecomeOneAxis()
    {
        var rule = new MultiSourceMapRule
        {
            Id = "row",
            Sources =
            [
                new MapSource { Kind = MapSourceKind.Button, Button = ButtonId.DpadRight },
                new MapSource { Kind = MapSourceKind.Button, Button = ButtonId.DpadLeft }
            ],
            CombineMode = CombineMode.Formula,
            Formula = "s1 - s2",
            TargetKind = MapTargetKind.StickAxisX,
            TargetStick = StickId.Left
        };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        Assert.Equal(1f, pipeline.Process(FrameWithButtons(ButtonId.DpadRight), now).VirtualSnapshot.LeftStick.X, precision: 3);
        Assert.Equal(-1f, pipeline.Process(FrameWithButtons(ButtonId.DpadLeft), now.AddMilliseconds(16)).VirtualSnapshot.LeftStick.X, precision: 3);
        Assert.Equal(0f, pipeline.Process(FrameWithButtons(), now.AddMilliseconds(32)).VirtualSnapshot.LeftStick.X, precision: 3);
    }

    [Fact]
    public void Process_MultiSource_AxisTargetPreservesTheSiblingAxis()
    {
        var rule = new MultiSourceMapRule
        {
            Id = "row",
            Sources = [new MapSource { Kind = MapSourceKind.Button, Button = ButtonId.South }],
            CombineMode = CombineMode.Maximum,
            TargetKind = MapTargetKind.StickAxisX,
            TargetStick = StickId.Left
        };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });

        var frame = new ControllerSnapshot
        {
            Buttons = FrameWithButtons(ButtonId.South).Buttons,
            LeftStick = new StickVector(0f, 0.6f)
        };
        var result = pipeline.Process(frame, DateTimeOffset.UtcNow);
        Assert.Equal(1f, result.VirtualSnapshot.LeftStick.X, precision: 3);
        Assert.Equal(0.6f, result.VirtualSnapshot.LeftStick.Y, precision: 3); // untouched
    }

    [Fact]
    public void Process_MultiSource_SuppressSourcesAndInvert()
    {
        var rule = new MultiSourceMapRule
        {
            Id = "row",
            Sources = [new MapSource { Kind = MapSourceKind.Button, Button = ButtonId.South, Invert = true }],
            CombineMode = CombineMode.Maximum,
            TargetKind = MapTargetKind.Button,
            TargetButton = ButtonId.North,
            SuppressSources = true
        };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        // South NOT pressed: inverted read = 1 -> North pressed.
        var idle = pipeline.Process(FrameWithButtons(), now);
        Assert.True(idle.VirtualSnapshot.IsPressed(ButtonId.North));

        // South pressed: inverted read = 0 -> North released; South itself suppressed from the output.
        var pressed = pipeline.Process(FrameWithButtons(ButtonId.South), now.AddMilliseconds(16));
        Assert.False(pressed.VirtualSnapshot.IsPressed(ButtonId.North));
        Assert.False(pressed.VirtualSnapshot.IsPressed(ButtonId.South));
    }

    private static ControllerSnapshot TouchFrame(bool down, float x, float y)
    {
        return new ControllerSnapshot { Buttons = ButtonState.CreateEmptyMap(), TouchDown = down, TouchX = x, TouchY = y };
    }

    [Fact]
    public void Process_TouchpadStick_AnchorsWhereFingerFirstLands()
    {
        var rule = new TouchpadMapRule
        {
            Id = "pad",
            StickEnabled = true,
            TargetStick = StickId.Right,
            StickSensitivity = 2.0f,
            DpadEnabled = false
        };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        // Touch down off-center (0.7, 0.5) — this becomes the anchor, so
        // the stick should read ZERO right here, not jump toward (0.7,0.5).
        var touchDown = pipeline.Process(TouchFrame(true, 0.7f, 0.5f), now);
        Assert.Equal(0f, touchDown.VirtualSnapshot.RightStick.X, precision: 3);
        Assert.Equal(0f, touchDown.VirtualSnapshot.RightStick.Y, precision: 3);

        // Drag 0.1 to the right of the anchor: dx=0.1 * sensitivity 2.0 = 0.2.
        var dragged = pipeline.Process(TouchFrame(true, 0.8f, 0.5f), now.AddMilliseconds(16));
        Assert.Equal(0.2f, dragged.VirtualSnapshot.RightStick.X, precision: 3);
        Assert.Equal(0f, dragged.VirtualSnapshot.RightStick.Y, precision: 3);

        // Lift the finger: stick returns to zero.
        var released = pipeline.Process(TouchFrame(false, 0.8f, 0.5f), now.AddMilliseconds(32));
        Assert.Equal(0f, released.VirtualSnapshot.RightStick.X, precision: 3);

        // Touch down again at a NEW spot — re-anchors, doesn't reuse the old anchor.
        var reanchored = pipeline.Process(TouchFrame(true, 0.2f, 0.2f), now.AddMilliseconds(48));
        Assert.Equal(0f, reanchored.VirtualSnapshot.RightStick.X, precision: 3);
        Assert.Equal(0f, reanchored.VirtualSnapshot.RightStick.Y, precision: 3);
    }

    [Fact]
    public void Process_TouchpadStick_YIsUpPositive_MatchingStickConvention()
    {
        var rule = new TouchpadMapRule { Id = "pad", StickEnabled = true, TargetStick = StickId.Left, StickSensitivity = 1.0f };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        _ = pipeline.Process(TouchFrame(true, 0.5f, 0.5f), now);
        // SDL touch Y grows DOWNWARD (0=top); dragging toward the top of
        // the pad (smaller Y) should push the stick Y POSITIVE (up),
        // matching this codebase's own stick convention.
        var draggedUp = pipeline.Process(TouchFrame(true, 0.5f, 0.3f), now.AddMilliseconds(16));
        Assert.True(draggedUp.VirtualSnapshot.LeftStick.Y > 0f);
    }

    [Theory]
    [InlineData(0.1f, 0f, ButtonId.DpadRight)]     // due east
    [InlineData(0f, 0.1f, ButtonId.DpadUp)]        // due north (touch-Y decreasing -> dy positive after negation)
    [InlineData(-0.1f, 0f, ButtonId.DpadLeft)]     // due west
    [InlineData(0f, -0.1f, ButtonId.DpadDown)]     // due south
    public void Process_TouchpadDpad_FourCardinalWedges(float dx, float dy, ButtonId expected)
    {
        var rule = new TouchpadMapRule { Id = "pad", StickEnabled = false, DpadEnabled = true, DpadDeadzoneRadius = 0.02f, DpadEightWay = false };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        _ = pipeline.Process(TouchFrame(true, 0.5f, 0.5f), now);
        // dy here is the desired POST-negation delta; touch-space Y moves opposite.
        var result = pipeline.Process(TouchFrame(true, 0.5f + dx, 0.5f - dy), now.AddMilliseconds(16));

        foreach (var candidate in new[] { ButtonId.DpadUp, ButtonId.DpadDown, ButtonId.DpadLeft, ButtonId.DpadRight })
        {
            Assert.Equal(candidate == expected, result.VirtualSnapshot.IsPressed(candidate));
        }
    }

    [Fact]
    public void Process_TouchpadDpad_EightWayDiagonalHoldsTwoButtons()
    {
        var rule = new TouchpadMapRule { Id = "pad", StickEnabled = false, DpadEnabled = true, DpadDeadzoneRadius = 0.02f, DpadEightWay = true };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        _ = pipeline.Process(TouchFrame(true, 0.5f, 0.5f), now);
        // Up-right: dx positive, touch-Y decreasing (-> dy positive post-negation).
        var result = pipeline.Process(TouchFrame(true, 0.6f, 0.4f), now.AddMilliseconds(16));

        Assert.True(result.VirtualSnapshot.IsPressed(ButtonId.DpadUp));
        Assert.True(result.VirtualSnapshot.IsPressed(ButtonId.DpadRight));
        Assert.False(result.VirtualSnapshot.IsPressed(ButtonId.DpadDown));
        Assert.False(result.VirtualSnapshot.IsPressed(ButtonId.DpadLeft));
    }

    [Fact]
    public void Process_TouchpadDpad_BelowDeadzone_NoDirectionFires()
    {
        var rule = new TouchpadMapRule { Id = "pad", StickEnabled = false, DpadEnabled = true, DpadDeadzoneRadius = 0.1f };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        _ = pipeline.Process(TouchFrame(true, 0.5f, 0.5f), now);
        // Tiny 0.02 drag, well under the 0.1 deadzone.
        var result = pipeline.Process(TouchFrame(true, 0.52f, 0.5f), now.AddMilliseconds(16));

        foreach (var candidate in new[] { ButtonId.DpadUp, ButtonId.DpadDown, ButtonId.DpadLeft, ButtonId.DpadRight })
        {
            Assert.False(result.VirtualSnapshot.IsPressed(candidate));
        }
    }

    [Fact]
    public void Process_TouchpadMouse_FirstContactTickProducesNoDelta()
    {
        var rule = new TouchpadMapRule { Id = "pad", StickEnabled = false, DpadEnabled = false, MouseEnabled = true };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        // There's no "previous position" yet on the very first contact
        // tick — this must NOT read as a teleport from (0,0).
        var firstTouch = pipeline.Process(TouchFrame(true, 0.5f, 0.5f), now);
        Assert.Equal(0f, firstTouch.MouseDeltaX, precision: 3);
        Assert.Equal(0f, firstTouch.MouseDeltaY, precision: 3);
    }

    [Fact]
    public void Process_TouchpadMouse_IsFrameToFrameNotAnchorRelative()
    {
        var rule = new TouchpadMapRule { Id = "pad", StickEnabled = false, DpadEnabled = false, MouseEnabled = true, MouseSensitivityX = 1f, MouseSensitivityY = 1f };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        _ = pipeline.Process(TouchFrame(true, 0.5f, 0.5f), now);
        var step1 = pipeline.Process(TouchFrame(true, 0.6f, 0.5f), now.AddMilliseconds(16));
        Assert.Equal(60f, step1.MouseDeltaX, precision: 1); // 0.1 * sensitivity 1.0 * 600 reference px

        // A SECOND identical-sized step should produce the SAME delta
        // again (frame-to-frame) — an anchor-relative reading would
        // instead report 0.2's worth (0.7 - 0.5 anchor), i.e. double.
        var step2 = pipeline.Process(TouchFrame(true, 0.7f, 0.5f), now.AddMilliseconds(32));
        Assert.Equal(60f, step2.MouseDeltaX, precision: 1);
    }

    [Fact]
    public void Process_TouchpadMouse_YIsScreenDownPositive_UnlikeStickConvention()
    {
        var rule = new TouchpadMapRule { Id = "pad", StickEnabled = false, DpadEnabled = false, MouseEnabled = true };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        _ = pipeline.Process(TouchFrame(true, 0.5f, 0.5f), now);
        // Touch Y increasing (finger moves toward the BOTTOM of the pad)
        // should give a POSITIVE mouse delta Y (cursor moves down) — NOT
        // negated the way the stick output is.
        var draggedDown = pipeline.Process(TouchFrame(true, 0.5f, 0.6f), now.AddMilliseconds(16));
        Assert.True(draggedDown.MouseDeltaY > 0f);
    }

    [Fact]
    public void Process_TouchpadMouse_SensitivityAndInvertApply()
    {
        var rule = new TouchpadMapRule
        {
            Id = "pad", StickEnabled = false, DpadEnabled = false, MouseEnabled = true,
            MouseSensitivityX = 2f, InvertMouseX = true
        };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        _ = pipeline.Process(TouchFrame(true, 0.5f, 0.5f), now);
        var result = pipeline.Process(TouchFrame(true, 0.6f, 0.5f), now.AddMilliseconds(16));

        // 0.1 * sensitivity 2.0 * 600 reference px = 120, inverted -> -120.
        Assert.Equal(-120f, result.MouseDeltaX, precision: 1);
    }

    [Fact]
    public void Process_TouchpadMouse_StopsAccumulating_WhenFingerLifted()
    {
        var rule = new TouchpadMapRule { Id = "pad", StickEnabled = false, DpadEnabled = false, MouseEnabled = true };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        _ = pipeline.Process(TouchFrame(true, 0.5f, 0.5f), now);
        _ = pipeline.Process(TouchFrame(true, 0.6f, 0.5f), now.AddMilliseconds(16));

        var released = pipeline.Process(TouchFrame(false, 0.6f, 0.5f), now.AddMilliseconds(32));
        Assert.Equal(0f, released.MouseDeltaX, precision: 3);

        // Re-touching at a totally different spot must NOT report the
        // jump as a delta (same first-contact gate as the anchor tests).
        var retouch = pipeline.Process(TouchFrame(true, 0.1f, 0.1f), now.AddMilliseconds(48));
        Assert.Equal(0f, retouch.MouseDeltaX, precision: 3);
        Assert.Equal(0f, retouch.MouseDeltaY, precision: 3);
    }

    [Fact]
    public void Process_TouchpadMouse_CoexistsWithStickMode_FromTheSameRule()
    {
        var rule = new TouchpadMapRule
        {
            Id = "pad", StickEnabled = true, TargetStick = StickId.Right, StickSensitivity = 1f,
            DpadEnabled = false, MouseEnabled = true, MouseSensitivityX = 1f, MouseSensitivityY = 1f
        };
        using var pipeline = new ControllerMappingPipeline(new ProfileDocument { Rules = [rule] });
        var now = DateTimeOffset.UtcNow;

        _ = pipeline.Process(TouchFrame(true, 0.5f, 0.5f), now);
        var result = pipeline.Process(TouchFrame(true, 0.6f, 0.5f), now.AddMilliseconds(16));

        // Stick reads anchor-relative (0.6 - 0.5 anchor = 0.1 * sensitivity 1.0).
        Assert.Equal(0.1f, result.VirtualSnapshot.RightStick.X, precision: 3);
        // Mouse reads frame-to-frame (same 0.1 step here, since it's the first step after anchoring).
        Assert.Equal(60f, result.MouseDeltaX, precision: 1);
    }
}
