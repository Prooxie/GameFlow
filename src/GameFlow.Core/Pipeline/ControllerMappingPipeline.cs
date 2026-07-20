using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using GameFlow.Core.Scripting;

namespace GameFlow.Core.Pipeline;

/// <param name="profile">The active profile whose rules this instance executes.</param>
/// <param name="scriptEngine">
/// Optional. When null (the default — every existing call site keeps
/// compiling and behaving exactly as before), <see cref="ControlScriptRule"/>
/// and <see cref="MultiSourceCombineRule"/> rows in <see cref="CombineMode.Script"/>
/// mode are silently skipped rather than throwing, since there's nowhere
/// to run their Lua. Pass a real engine to light both up.
/// </param>
public sealed class ControllerMappingPipeline(ProfileDocument profile, LuaScriptEngine? scriptEngine = null)
{
    private readonly ProfileDocument profile = profile;
    private readonly LuaScriptEngine? scriptEngine = scriptEngine;
    private readonly Dictionary<string, StickPulseScheduler> stickAutofireSchedulers = [];
    private readonly Dictionary<string, StickPulseScheduler> freezeSchedulers = [];
    private readonly Dictionary<string, BinaryPulseScheduler> buttonAutofireSchedulers = [];
    private readonly Dictionary<string, FreezeLatch> freezeLatches = [];

    /// <summary>
    /// Per-rule executor for looping multi-button autofire. Keyed by
    /// rule id so swapping a rule in/out of the profile rebuilds the
    /// state for that rule only — and never bleeds timeline state
    /// between two rules with the same trigger.
    /// </summary>
    private readonly Dictionary<string, MultiButtonAutofireExecutor> multiButtonExecutors = [];

    /// <summary>
    /// Per-rule executor for <see cref="MultiSourceCombineRule"/>, same
    /// keyed-by-rule-id lifetime rationale as <see cref="multiButtonExecutors"/>
    /// above — only <see cref="CombineMode.ToggleOnAny"/> actually carries
    /// state, but every combine rule gets one for a uniform lookup.
    /// </summary>
    private readonly Dictionary<string, MultiSourceCombineExecutor> combineExecutors = [];

    /// <summary>
    /// <see cref="ButtonId"/> has no gaps in its ordinals (see that enum's
    /// own comment about why <c>None</c> must stay first), so a plain
    /// array indexed by <c>(int)ButtonId</c> is a safe, allocation-cheap
    /// stand-in for the dictionary when calling into
    /// <see cref="LuaScriptEngine.Execute"/>, which was written against
    /// that array shape.
    /// </summary>
    private static readonly int ButtonCount = Enum.GetValues<ButtonId>().Length;

    /// <summary>
    /// Runtime "is this rule currently muted" overlay maintained by
    /// <see cref="RuleToggleRule"/> executions. Targets that appear
    /// here are skipped by every <c>.Where(IsActive)</c> filter below.
    /// Resets to empty on each app launch (not persisted) — the
    /// profile JSON's Enabled flag stays the source of truth across
    /// restarts.
    /// </summary>
    private readonly HashSet<string> runtimeDisabledIds = [];

    /// <summary>
    /// Last-frame state of each rule-toggle's source button, used for
    /// rising-edge detection. Keyed by rule id (not button id) so two
    /// toggle rules sharing the same trigger don't fight over the
    /// edge.
    /// </summary>
    private readonly Dictionary<string, bool> toggleSourceWasPressed = [];

    // Issue #12: Track previous button states for rising-edge detection on freeze rules.
    private readonly Dictionary<ButtonId, bool> previousButtonStates = [];

    /// <summary>
    /// Helper that combines a rule's authored Enabled flag with the
    /// runtime-disabled overlay maintained by <see cref="RuleToggleRule"/>
    /// executions. Used in every <c>.Where(...)</c> below so toggle
    /// state actually mutes downstream rule processing.
    /// </summary>
    private bool IsActive(MappingRule rule) =>
        rule.Enabled && !runtimeDisabledIds.Contains(rule.Id);

    public ControllerFrameResult Process(ControllerSnapshot physical, DateTimeOffset now)
    {
        var notes = new List<string>();
        var buttons = ButtonState.Clone(physical.Buttons);

        // RuleToggleRule pass — must run before any rule whose Enabled
        // state it might flip. On the rising edge of the toggle's
        // source button, every target id is flipped in/out of the
        // runtime-disabled set, which the IsActive helper consults
        // below.
        foreach (var rule in profile.Rules.OfType<RuleToggleRule>().Where(rule => rule.Enabled))
        {
            if (rule.SourceButton == ButtonId.None) { continue; }

            var pressed = physical.IsPressed(rule.SourceButton);
            toggleSourceWasPressed.TryGetValue(rule.Id, out var wasPressed);

            if (pressed && !wasPressed)
            {
                foreach (var targetId in rule.TargetRuleIds)
                {
                    if (string.IsNullOrWhiteSpace(targetId)) { continue; }
                    if (!runtimeDisabledIds.Remove(targetId))
                    {
                        runtimeDisabledIds.Add(targetId);
                    }
                }
                notes.Add($"Toggled {rule.TargetRuleIds.Count} rule(s) via {rule.SourceButton}.");
            }
            toggleSourceWasPressed[rule.Id] = pressed;

            if (rule.SuppressSourceButton)
            {
                buttons[rule.SourceButton] = false;
            }
        }

        // Issue #10: Use the threshold-adjusted map as the baseline for the output sticks.
        // Previously leftStick/rightStick were seeded from physical.LeftStick/RightStick,
        // which meant StickThresholdRule deadzones only affected autofire source reads,
        // not the final virtual output. Now thresholds apply to the output directly.
        var transformedSticks = BuildSourceStickMap(physical);
        var leftStick = transformedSticks[StickId.Left];
        var rightStick = transformedSticks[StickId.Right];

        // Previously these were never read back — the final snapshot always
        // carried physical.LeftTrigger/RightTrigger through untouched, so no
        // rule kind could actually change a trigger. Mutable locals here,
        // fed into the final snapshot below, are what any trigger-writing
        // rule kind (ControlScriptRule today; a future Stick-Trim-style
        // rule later) actually writes into.
        var leftTrigger = physical.LeftTrigger;
        var rightTrigger = physical.RightTrigger;

        foreach (var rule in profile.Rules.OfType<ButtonRemapRule>().Where(IsActive))
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

        foreach (var rule in profile.Rules.OfType<ButtonAutofireRule>().Where(IsActive))
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

        foreach (var rule in profile.Rules.OfType<MultiButtonAutofireRule>().Where(IsActive))
        {
            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }
            if (rule.Mode == RuleMode.DoNothing)
            {
                if (rule.SourceButton != ButtonId.None)
                {
                    buttons[rule.SourceButton] = false;
                }
                continue;
            }

            if (!multiButtonExecutors.TryGetValue(rule.Id, out var executor))
            {
                executor = new MultiButtonAutofireExecutor(rule);
                multiButtonExecutors[rule.Id] = executor;
            }
            executor.Apply(physical, buttons, now);
        }

        // Runs after every other button-producing rule kind so a combine
        // row can list an already-remapped/autofired/combo'd button as one
        // of its own sources — see MultiSourceCombineRule's class comment.
        foreach (var rule in profile.Rules.OfType<MultiSourceCombineRule>().Where(IsActive))
        {
            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }

            if (rule.Mode == RuleMode.DoNothing)
            {
                if (rule.TargetButton != ButtonId.None)
                {
                    buttons[rule.TargetButton] = false;
                }
                continue;
            }

            if (rule.Strategy == CombineMode.Script && rule.TargetButton != ButtonId.None && scriptEngine is null)
            {
                notes.Add($"Combine rule '{rule.Name}' uses the Script strategy but no script engine is attached — target left untouched.");
            }

            if (!combineExecutors.TryGetValue(rule.Id, out var combineExecutor))
            {
                combineExecutor = new MultiSourceCombineExecutor(rule);
                combineExecutors[rule.Id] = combineExecutor;
            }
            combineExecutor.Apply(buttons, scriptEngine, now);
        }

        foreach (var rule in profile.Rules.OfType<StickAutofireRule>().Where(IsActive))
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

        foreach (var rule in profile.Rules.OfType<FreezeLastDirectionRule>().Where(IsActive))
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

        // Runs last, after every deterministic rule kind, so a script sees
        // the fully-composed virtual state (remaps, autofire, combos,
        // combine rows, stick handling — everything) and can layer its own
        // adjustments on top or override outright, same "already-remapped
        // button" guarantee as every other rule kind gives the ones after it.
        if (scriptEngine is not null)
        {
            var scriptRules = profile.Rules.OfType<ControlScriptRule>().Where(IsActive).ToList();
            if (scriptRules.Count > 0)
            {
                var virtualBefore = physical
                    .WithStick(StickId.Left, leftStick.Clamp())
                    .WithStick(StickId.Right, rightStick.Clamp())
                    .WithButtons(buttons)
                    .WithTriggers(leftTrigger, rightTrigger);

                var buttonArray = ButtonsToArray(buttons);

                foreach (var rule in scriptRules)
                {
                    scriptEngine.Execute(rule, physical, virtualBefore, buttonArray,
                        ref leftStick, ref rightStick, ref leftTrigger, ref rightTrigger, now);
                }

                ArrayToButtons(buttonArray, buttons);
            }
        }

        var virtualSnapshot = physical
            .WithStick(StickId.Left, leftStick.Clamp())
            .WithStick(StickId.Right, rightStick.Clamp())
            .WithButtons(buttons)
            .WithTriggers(leftTrigger, rightTrigger)
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

        foreach (var rule in profile.Rules.OfType<StickThresholdRule>().Where(rule => IsActive(rule) && rule.Mode != RuleMode.Passthrough))
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

    private static bool[] ButtonsToArray(Dictionary<ButtonId, bool> buttons)
    {
        var array = new bool[ButtonCount];
        foreach (var (button, pressed) in buttons)
        {
            array[(int)button] = pressed;
        }
        return array;
    }

    private static void ArrayToButtons(bool[] array, Dictionary<ButtonId, bool> buttons)
    {
        for (var i = 0; i < array.Length; i++)
        {
            buttons[(ButtonId)i] = array[i];
        }
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
