using Autofire.Core.Enums;
using Autofire.Core.Models;
using Autofire.Core.Models.Rules;

namespace Autofire.Core.Pipeline;

public sealed class ControllerMappingPipeline(ProfileDocument profile)
{
    private readonly ProfileDocument profile = profile;
    private readonly Dictionary<string, StickPulseScheduler> stickAutofireSchedulers = [];
    private readonly Dictionary<string, StickPulseScheduler> freezeSchedulers = [];
    private readonly Dictionary<string, BinaryPulseScheduler> buttonAutofireSchedulers = [];
    private readonly Dictionary<string, FreezeLatch> freezeLatches = [];

    // Issue #12: Track previous button states for rising-edge detection on freeze rules.
    private readonly Dictionary<ButtonId, bool> previousButtonStates = [];

    public ControllerFrameResult Process(ControllerSnapshot physical, DateTimeOffset now)
    {
        var notes = new List<string>();
        var buttons = ButtonState.Clone(physical.Buttons);

        // Issue #10: Use the threshold-adjusted map as the baseline for the output sticks.
        // Previously leftStick/rightStick were seeded from physical.LeftStick/RightStick,
        // which meant StickThresholdRule deadzones only affected autofire source reads,
        // not the final virtual output. Now thresholds apply to the output directly.
        var transformedSticks = BuildSourceStickMap(physical);
        var leftStick = transformedSticks[StickId.Left];
        var rightStick = transformedSticks[StickId.Right];

        foreach (var rule in profile.Rules.OfType<ButtonRemapRule>().Where(rule => rule.Enabled))
        {
            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }

            if (rule.Mode == RuleMode.DoNothing)
            {
                buttons[rule.SourceButton] = false;
                notes.Add($"Blocked {rule.SourceButton}.");
                continue;
            }

            if (!physical.IsPressed(rule.SourceButton))
            {
                continue;
            }

            buttons[rule.TargetButton] = true;
            if (rule.SuppressSourceButton)
            {
                buttons[rule.SourceButton] = false;
            }

            notes.Add($"Remapped {rule.SourceButton} -> {rule.TargetButton}.");
        }

        foreach (var rule in profile.Rules.OfType<ButtonAutofireRule>().Where(rule => rule.Enabled))
        {
            var scheduler = GetOrCreateBinaryScheduler(rule);
            scheduler.SetDesired(physical.IsPressed(rule.SourceButton), now);
            var pulse = scheduler.Tick(now);

            if (rule.Mode == RuleMode.DoNothing)
            {
                buttons[rule.SourceButton] = false;
                continue;
            }

            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }

            if (pulse.IsPressed)
            {
                buttons[rule.TargetButton] = true;
            }

            if (rule.SuppressSourceButton)
            {
                buttons[rule.SourceButton] = false;
            }
        }

        foreach (var rule in profile.Rules.OfType<StickAutofireRule>().Where(rule => rule.Enabled))
        {
            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }

            if (rule.Mode == RuleMode.DoNothing)
            {
                SetStick(ref leftStick, ref rightStick, rule.TargetStick, StickVector.Zero);
                continue;
            }

            var source = transformedSticks[rule.SourceStick]
                .WithDeadzone(rule.ActivationDeadzone)
                .AmplifyToFull(rule.ActivationFullAt);
            var scheduler = GetOrCreateStickScheduler(rule);
            scheduler.SetDesired(source, now);
            var pulse = scheduler.Tick(now).Value;

            var current = GetStick(leftStick, rightStick, rule.TargetStick);
            var merged = ApplyBlend(current, pulse, rule.BlendMode);
            SetStick(ref leftStick, ref rightStick, rule.TargetStick, merged);

            if (rule.SuppressSourceStick)
            {
                SetStick(ref leftStick, ref rightStick, rule.SourceStick, StickVector.Zero);
            }
        }

        foreach (var rule in profile.Rules.OfType<FreezeLastDirectionRule>().Where(rule => rule.Enabled))
        {
            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }

            if (rule.Mode == RuleMode.DoNothing)
            {
                SetStick(ref leftStick, ref rightStick, rule.TargetStick, StickVector.Zero);
                if (rule.SuppressActivationButton)
                {
                    buttons[rule.ActivationButton] = false;
                }
                continue;
            }

            var latch = GetOrCreateFreezeLatch(rule);
            var buttonNowPressed = physical.IsPressed(rule.ActivationButton);
            var buttonWasPressed = previousButtonStates.GetValueOrDefault(rule.ActivationButton);
            previousButtonStates[rule.ActivationButton] = buttonNowPressed;

            // Issue #12: Capture the stick vector only at the RISING EDGE of the
            // activation button. This means the latch holds exactly what the stick
            // was doing the instant you pressed the button (including zero / idle),
            // rather than the last-ever non-zero historical position.
            if (buttonNowPressed && !buttonWasPressed)
            {
                latch.CaptureSnapshot(transformedSticks[rule.CaptureStick]);
            }

            if (buttonNowPressed)
            {
                var frozenVector = latch.Current;

                if (rule.PulseEnabled)
                {
                    var scheduler = GetOrCreateFreezeScheduler(rule);
                    scheduler.SetDesired(frozenVector, now);
                    frozenVector = scheduler.Tick(now).Value;
                }

                var currentTarget = GetStick(leftStick, rightStick, rule.TargetStick);
                var merged = ApplyBlend(currentTarget, frozenVector, rule.BlendMode);
                SetStick(ref leftStick, ref rightStick, rule.TargetStick, merged);

                if (rule.SuppressActivationButton)
                {
                    buttons[rule.ActivationButton] = false;
                }

                if (rule.SuppressCaptureStick)
                {
                    SetStick(ref leftStick, ref rightStick, rule.CaptureStick, StickVector.Zero);
                }
            }
            else
            {
                // Button released: clear the freeze scheduler so the next press starts fresh.
                if (freezeSchedulers.TryGetValue(rule.Id, out var scheduler))
                {
                    scheduler.Clear();
                }
            }
        }

        var virtualSnapshot = physical
            .WithStick(StickId.Left, leftStick.Clamp())
            .WithStick(StickId.Right, rightStick.Clamp())
            .WithButtons(buttons)
            with
            {
                Timestamp = now,
                DeviceName = $"{physical.DeviceName} / virtual"
            };

        return new ControllerFrameResult(physical with { Timestamp = now }, virtualSnapshot, notes);
    }

    /// <summary>
    /// Builds a threshold-adjusted stick map. This map now also seeds the baseline
    /// output stick values (leftStick, rightStick) so that StickThresholdRule
    /// deadzones and amplification are reflected in the final virtual output.
    /// </summary>
    private Dictionary<StickId, StickVector> BuildSourceStickMap(ControllerSnapshot snapshot)
    {
        var map = new Dictionary<StickId, StickVector>
        {
            [StickId.Left] = snapshot.LeftStick,
            [StickId.Right] = snapshot.RightStick
        };

        foreach (var rule in profile.Rules.OfType<StickThresholdRule>().Where(rule => rule.Enabled && rule.Mode != RuleMode.Passthrough))
        {
            if (rule.Mode == RuleMode.DoNothing)
            {
                map[rule.TargetStick] = StickVector.Zero;
                continue;
            }

            map[rule.TargetStick] = map[rule.TargetStick]
                .WithDeadzone(rule.Deadzone)
                .AmplifyToFull(rule.FullAt);
        }

        return map;
    }

    private static StickVector ApplyBlend(StickVector current, StickVector pulse, StickBlendMode blendMode)
    {
        return blendMode switch
        {
            StickBlendMode.Replace => pulse.Clamp(),
            StickBlendMode.Additive => (current + pulse).Clamp(),
            _ => current
        };
    }

    private static StickVector GetStick(StickVector leftStick, StickVector rightStick, StickId stickId)
    {
        return stickId == StickId.Left ? leftStick : rightStick;
    }

    private static void SetStick(ref StickVector leftStick, ref StickVector rightStick, StickId stickId, StickVector value)
    {
        if (stickId == StickId.Left)
        {
            leftStick = value;
            return;
        }

        rightStick = value;
    }

    private StickPulseScheduler GetOrCreateStickScheduler(StickAutofireRule rule)
    {
        return stickAutofireSchedulers.TryGetValue(rule.Id, out var scheduler)
                ? scheduler
                : stickAutofireSchedulers[rule.Id] = new StickPulseScheduler(rule.Timing);
    }

    private StickPulseScheduler GetOrCreateFreezeScheduler(FreezeLastDirectionRule rule)
    {
        return freezeSchedulers.TryGetValue(rule.Id, out var scheduler)
                ? scheduler
                : freezeSchedulers[rule.Id] = new StickPulseScheduler(rule.Timing);
    }

    private BinaryPulseScheduler GetOrCreateBinaryScheduler(ButtonAutofireRule rule)
    {
        return buttonAutofireSchedulers.TryGetValue(rule.Id, out var scheduler)
                ? scheduler
                : buttonAutofireSchedulers[rule.Id] = new BinaryPulseScheduler(rule.Timing);
    }

    private FreezeLatch GetOrCreateFreezeLatch(FreezeLastDirectionRule rule)
    {
        return freezeLatches.TryGetValue(rule.Id, out var latch)
                ? latch
                : freezeLatches[rule.Id] = new FreezeLatch();
    }
}
