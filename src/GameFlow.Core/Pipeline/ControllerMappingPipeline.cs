using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using GameFlow.Core.Scripting;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameFlow.Core.Pipeline;

public sealed class ControllerMappingPipeline(ProfileDocument profile) : IDisposable
{
    private readonly ProfileDocument profile = profile;

    /// <summary>
    /// Owned per pipeline (i.e. per slot / per active profile), matching
    /// every other piece of per-rule execution state below (schedulers,
    /// latches) -- a script's compiled bytecode and its persistent Lua
    /// `state` table are meaningless shared across two different slots.
    /// ControlScriptRule.cs's own doc comment: "Runtime execution is
    /// intentionally handled by higher layers" -- this IS that layer.
    /// </summary>
    private readonly LuaScriptEngine luaScriptEngine = new(NullLogger<LuaScriptEngine>.Instance);
    private readonly ShiftLayerResolver shiftLayerResolver = new();
    private readonly GyroProcessor gyroProcessor = new();

    /// <summary>Previous tick time, for converting the gyro's angular RATE into this tick's mouse movement.</summary>
    private DateTimeOffset lastGyroTickAt = DateTimeOffset.MinValue;

    private bool disposed;
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

    /// <summary>Per-rule executor for ButtonComboRule — same keyed-by-rule-id lifecycle as <see cref="multiButtonExecutors"/>.</summary>
    private readonly Dictionary<string, ButtonComboExecutor> comboExecutors = [];

    /// <summary>Per-rule SOCD conflict-tracking state, keyed by rule id.</summary>
    private sealed class SocdState
    {
        public ButtonId? CurrentFirst;
        public bool NegativeWasPressed;
        public bool PositiveWasPressed;
    }
    private readonly Dictionary<string, SocdState> socdStates = [];

    private SocdState GetOrCreateSocdState(string ruleId) =>
        socdStates.TryGetValue(ruleId, out var state) ? state : socdStates[ruleId] = new SocdState();

    /// <summary>Per-rule touch anchor point + previous-tick down state, keyed by rule id.</summary>
    private sealed class TouchAnchorState
    {
        public float AnchorX;
        public float AnchorY;
        public bool WasDown;

        /// <summary>
        /// Previous tick's raw touch position, for the mouse mode's
        /// frame-to-frame delta — DELIBERATELY separate from
        /// AnchorX/AnchorY, which stay fixed at the touch-down point for
        /// the stick/D-pad modes. Mouse movement is relative motion
        /// (how a laptop touchpad works), not anchor-relative.
        /// </summary>
        public float PrevMouseX;
        public float PrevMouseY;
    }
    private readonly Dictionary<string, TouchAnchorState> touchAnchors = [];

    /// <summary>
    /// Screen pixels a full 0..1 (edge-to-edge) touchpad sweep moves the
    /// cursor at MouseSensitivityX/Y = 1.0. Touchpads are physically
    /// small, so this favors a smaller-than-edge-to-edge swipe covering
    /// a meaningful chunk of a typical monitor over requiring a full
    /// sweep for any real movement; sensitivity scales linearly from
    /// here. Not derived from spec (no pixel figure was given) — a
    /// documented, tunable starting point.
    /// </summary>
    private const float MouseDeltaReferencePixels = 600f;

    private TouchAnchorState GetOrCreateTouchAnchor(string ruleId) =>
        touchAnchors.TryGetValue(ruleId, out var state) ? state : touchAnchors[ruleId] = new TouchAnchorState();

    /// <summary>Per-rule gesture recognizer, keyed by rule id. Stateful across ticks — a stroke spans many.</summary>
    private readonly Dictionary<string, TouchGestureEngine> touchGestureEngines = [];

    private TouchGestureEngine GetOrCreateGestureEngine(string ruleId) =>
        touchGestureEngines.TryGetValue(ruleId, out var engine)
            ? engine
            : touchGestureEngines[ruleId] = new TouchGestureEngine();

    /// <summary>
    /// Buttons a gesture is currently holding down, and when each hold
    /// expires. A gesture is an instantaneous event but has to present to
    /// the game as a press with duration, so firing one schedules an
    /// expiry here and every subsequent tick re-asserts the button until
    /// that time passes.
    /// </summary>
    private readonly Dictionary<ButtonId, DateTimeOffset> gestureHoldsUntil = [];

    /// <summary>
    /// Scratch buffer for the contact list handed to the recognizer.
    /// Reused because this is rebuilt every tick at up to 1000 Hz per
    /// slot, and a fresh list each time is pure garbage.
    /// </summary>
    private readonly List<TouchContact> gestureContactBuffer = [];

    /// <summary>
    /// The frame's contacts as the recognizer should see them.
    ///
    /// <para>
    /// Sources that track every finger (SDL, and so every DualSense /
    /// DualShock 4) fill <see cref="ControllerSnapshot.TouchContacts"/>
    /// and are passed through untouched. Sources that report only a
    /// primary contact get a single synthetic finger built from
    /// <c>TouchX</c>/<c>TouchY</c> — enough for swipes, taps, long
    /// presses and shapes, all of which only ever follow one finger.
    /// Pinch, rotate and the two-to-five-finger gestures simply never
    /// fire there, which is the honest outcome: the position data they
    /// need was never reported. Note this deliberately does NOT consult
    /// <c>TouchContactCount</c> — a source can count fingers it cannot
    /// place, and inventing positions to match the count would fabricate
    /// pinches out of nothing.
    /// </para>
    /// </summary>
    private IReadOnlyList<TouchContact> ResolveGestureContacts(ControllerSnapshot physical)
    {
        if (physical.TouchContacts.Count > 0)
        {
            return physical.TouchContacts;
        }

        gestureContactBuffer.Clear();
        if (physical.TouchDown)
        {
            gestureContactBuffer.Add(new TouchContact(0, physical.TouchX, physical.TouchY));
        }
        return gestureContactBuffer;
    }

    /// <summary>
    /// Runs one rule's gesture recognizer for this tick and schedules a
    /// timed press for every binding the emitted gestures satisfy.
    ///
    /// <para>
    /// One gesture can satisfy several bindings, and several gestures can
    /// land on the same tick (a pinch and a rotate often do), so this
    /// walks the full cross product rather than stopping at the first
    /// hit. When two bindings target the SAME button, the longer hold
    /// wins — extending an in-flight press is harmless, whereas letting a
    /// shorter one overwrite it would cut the first gesture's press
    /// short.
    /// </para>
    /// </summary>
    private void ProcessGestures(
        TouchpadMapRule rule, ControllerSnapshot physical,
        ref ButtonMask produced, DateTimeOffset now)
    {
        if (!rule.GesturesEnabled || rule.Gestures.Count == 0 || rule.Mode == RuleMode.DoNothing)
        {
            return;
        }

        var engine = GetOrCreateGestureEngine(rule.Id);
        var gestures = engine.Tick(ResolveGestureContacts(physical), rule, now);
        if (gestures.Count == 0)
        {
            return;
        }

        for (var g = 0; g < gestures.Count; g++)
        {
            for (var b = 0; b < rule.Gestures.Count; b++)
            {
                var binding = rule.Gestures[b];
                if (binding.TargetButton == ButtonId.None
                    || !TouchGestureEngine.Matches(binding, gestures[g]))
                {
                    continue;
                }

                var until = now.AddMilliseconds(Math.Max(1, binding.HoldMilliseconds));
                if (!gestureHoldsUntil.TryGetValue(binding.TargetButton, out var existing) || until > existing)
                {
                    gestureHoldsUntil[binding.TargetButton] = until;
                }

                // Press on the firing tick too, not just from the next
                // one. At a low polling rate a tick is several
                // milliseconds, and deferring would shave that off the
                // front of every gesture press for no reason.
                produced[binding.TargetButton] = true;
            }
        }
    }

    /// <summary>
    /// Adds every button with an unexpired gesture press to the produced
    /// set and drops the entries that have run out. Expired holds are
    /// simply not added rather than forced off: the button may be
    /// genuinely held by the player's own thumb, and forcing it false
    /// would fight them.
    /// </summary>
    private void ApplyGestureHolds(ref ButtonMask buttons, DateTimeOffset now)
    {
        if (gestureHoldsUntil.Count == 0)
        {
            return;
        }

        List<ButtonId>? expired = null;
        foreach (var (button, until) in gestureHoldsUntil)
        {
            if (now < until)
            {
                buttons[button] = true;
            }
            else
            {
                // Collected rather than removed in place — mutating a
                // dictionary while enumerating it throws.
                (expired ??= []).Add(button);
            }
        }

        if (expired is not null)
        {
            foreach (var button in expired)
            {
                gestureHoldsUntil.Remove(button);
            }
        }
    }

    /// <summary>
    /// Buckets an anchor-relative touch vector into D-pad button(s) and
    /// ADDS them to <paramref name="buttons"/> (the produced set). It never
    /// clears a direction: the produced set is rebuilt every tick, so a
    /// stale press cannot linger, and clearing used to release the
    /// PHYSICAL D-pad too. Below <paramref name="deadzone"/>, nothing is
    /// added. 8-way diagonals hold two adjacent buttons at once (the
    /// standard way to represent 8 directions on 4 buttons).
    /// </summary>
    private static void ApplyWedgeDpad(float dx, float dy, float deadzone, bool eightWay, ref ButtonMask buttons)
    {
        if (MathF.Sqrt((dx * dx) + (dy * dy)) < deadzone)
        {
            return;
        }

        // atan2 convention here: 0deg = right (+X), 90deg = up (+Y), sweeping counter-clockwise, normalized to 0..360.
        var degrees = MathF.Atan2(dy, dx) * (180f / MathF.PI);
        if (degrees < 0f)
        {
            degrees += 360f;
        }

        if (!eightWay)
        {
            if (degrees is >= 315f or < 45f) { buttons[ButtonId.DpadRight] = true; }
            else if (degrees is >= 45f and < 135f) { buttons[ButtonId.DpadUp] = true; }
            else if (degrees is >= 135f and < 225f) { buttons[ButtonId.DpadLeft] = true; }
            else { buttons[ButtonId.DpadDown] = true; }
            return;
        }

        if (degrees is >= 337.5f or < 22.5f) { buttons[ButtonId.DpadRight] = true; }
        else if (degrees is >= 22.5f and < 67.5f) { buttons[ButtonId.DpadUp] = true; buttons[ButtonId.DpadRight] = true; }
        else if (degrees is >= 67.5f and < 112.5f) { buttons[ButtonId.DpadUp] = true; }
        else if (degrees is >= 112.5f and < 157.5f) { buttons[ButtonId.DpadUp] = true; buttons[ButtonId.DpadLeft] = true; }
        else if (degrees is >= 157.5f and < 202.5f) { buttons[ButtonId.DpadLeft] = true; }
        else if (degrees is >= 202.5f and < 247.5f) { buttons[ButtonId.DpadDown] = true; buttons[ButtonId.DpadLeft] = true; }
        else if (degrees is >= 247.5f and < 292.5f) { buttons[ButtonId.DpadDown] = true; }
        else { buttons[ButtonId.DpadDown] = true; buttons[ButtonId.DpadRight] = true; }
    }

    /// <summary>Stick Trim's current ramped output per rule id, persisted across ticks so the ramp has continuity.</summary>
    private readonly Dictionary<string, float> trimValues = [];

    /// <summary>Stick Trim's last-processed tick timestamp per rule id, for computing the per-tick ramp step (dt).</summary>
    private readonly Dictionary<string, DateTimeOffset> lastTrimTickAt = [];

    /// <summary>
    /// Compiled Formula evaluators per MultiSourceMapRule id. Null =
    /// compile failed (noted once, then inert). The pipeline is rebuilt
    /// on every profile save, so a formula can't change within one
    /// instance's life — rule.Id is a sufficient key.
    /// </summary>
    private readonly Dictionary<string, Func<float[], float>?> compiledFormulas = [];

    /// <summary>
    /// Runtime on/off overlay maintained by <see cref="RuleToggleRule"/>
    /// executions: rule id → the state a toggle last put it in. A rule
    /// absent from here follows its authored <see cref="MappingRule.Enabled"/>.
    ///
    /// <para>
    /// An explicit state per id rather than a "muted" set, because a set
    /// can only ever take a rule AWAY: a rule saved disabled could never
    /// be switched on by a toggle, which is half of what a toggle is for.
    /// Resets on each app launch (not persisted) — the profile JSON's
    /// Enabled flag stays the source of truth across restarts.
    /// </para>
    /// </summary>
    private readonly Dictionary<string, bool> runtimeEnabledOverrides = new(StringComparer.Ordinal);

    /// <summary>
    /// Bumped on every toggle flip. The resolved axis inversion depends on
    /// which inversion rules are on, so it is cached against this and the
    /// active layer instead of being re-folded on every tick.
    /// </summary>
    private int toggleVersion;

    /// <summary>Every rule by id, first occurrence winning — what a toggle needs to know a target's authored state.</summary>
    private readonly Dictionary<string, MappingRule> rulesById = BuildRulesById(profile.Rules);

    /// <summary>
    /// Last-frame state of each rule-toggle's source button, used for
    /// rising-edge detection. Keyed by rule id (not button id) so two
    /// toggle rules sharing the same trigger don't fight over the
    /// edge.
    /// </summary>
    private readonly Dictionary<string, bool> toggleSourceWasPressed = [];

    // ── Rules bucketed by type, materialized ONCE at construction ──
    // The profile is immutable for this pipeline's lifetime (a profile
    // change disposes this instance and builds a new one), so bucketing
    // here is always correct and never goes stale. Previously every
    // pass ran profile.Rules.OfType<T>().Where(IsActive) per tick: 14
    // passes x (OfType iterator + Where iterator + IsActive delegate)
    // was ~670k allocations/second at 1000 Hz across 16 slots, purely
    // to re-filter a list that never changes. Arrays also iterate
    // without an enumerator allocation, unlike List<T> through an
    // interface. Order within each bucket is preserved, so
    // last-write-wins rule precedence is unchanged.
    private readonly RuleToggleRule[] ruleToggleRules = profile.Rules.OfType<RuleToggleRule>().ToArray();
    private readonly ButtonRemapRule[] buttonRemapRules = profile.Rules.OfType<ButtonRemapRule>().ToArray();
    private readonly ButtonAutofireRule[] buttonAutofireRules = profile.Rules.OfType<ButtonAutofireRule>().ToArray();
    private readonly MultiButtonAutofireRule[] multiButtonRules = profile.Rules.OfType<MultiButtonAutofireRule>().ToArray();
    private readonly ButtonComboRule[] buttonComboRules = profile.Rules.OfType<ButtonComboRule>().ToArray();
    private readonly SocdCleanRule[] socdCleanRules = profile.Rules.OfType<SocdCleanRule>().ToArray();
    private readonly StickAutofireRule[] stickAutofireRules = profile.Rules.OfType<StickAutofireRule>().ToArray();
    private readonly FreezeLastDirectionRule[] freezeRules = profile.Rules.OfType<FreezeLastDirectionRule>().ToArray();
    private readonly StickTrimRule[] stickTrimRules = profile.Rules.OfType<StickTrimRule>().ToArray();
    private readonly GyroMapRule[] gyroRules = profile.Rules.OfType<GyroMapRule>().ToArray();
    private readonly TouchpadMapRule[] touchpadRules = profile.Rules.OfType<TouchpadMapRule>().ToArray();
    private readonly MultiSourceMapRule[] multiSourceRules = profile.Rules.OfType<MultiSourceMapRule>().ToArray();
    private readonly ControlScriptRule[] controlScriptRules = profile.Rules.OfType<ControlScriptRule>().ToArray();
    private readonly StickThresholdRule[] stickThresholdRules = profile.Rules.OfType<StickThresholdRule>().ToArray();
    private readonly AxisInvertRule[] axisInvertRules = profile.Rules.OfType<AxisInvertRule>().ToArray();

    /// <summary>Layers whose activator is hidden from the output — see <see cref="ShiftLayer.SuppressActivatorButton"/>. Usually empty.</summary>
    private readonly ShiftLayer[] layersSuppressingActivator = profile.ShiftLayers.Where(layer => layer.SuppressActivatorButton).ToArray();

    // ── Layer-over-Base override (see ShiftLayer) ──
    // Every rule's source inputs as one bit set, computed once: a layer
    // rule's are OR-ed into activeLayerInputs while its layer is active,
    // and a Base rule whose own set meets that one is skipped. Keyed by
    // reference, not by the record's value equality — two rules can be
    // field-for-field identical and still be two rules.
    private readonly Dictionary<string, LayerRuleInputs[]> layerRuleInputs = BuildLayerRuleInputs(profile.Rules);
    private readonly Dictionary<MappingRule, ulong> baseRuleInputs = BuildBaseRuleInputs(profile.Rules);

    /// <summary>Source inputs bound by the active layer's currently enabled rules; 0 on Base.</summary>
    private ulong activeLayerInputs;

    /// <summary>The axis inversion last resolved, and the layer/toggle state it was resolved for.</summary>
    private AxisInvert? resolvedAxisInvert;
    private string? resolvedAxisInvertLayerId;
    private int resolvedAxisInvertToggleVersion = -1;

    /// <summary>
    /// Note text for each entry of <see cref="buttonRemapRules"/>, formatted
    /// once at construction and indexed in lockstep with it.
    ///
    /// <para>
    /// A held remap re-emits its note on every single frame, and a blocking
    /// remap emits one unconditionally — so interpolating inline formatted
    /// the same handful of strings up to a thousand times a second per slot,
    /// to feed a diagnostic strip that only ever displays the latest frame.
    /// The text depends solely on the rule's buttons, which cannot change
    /// without a new pipeline being built.
    /// </para>
    /// </summary>
    private readonly string[] buttonRemapNotes = profile.Rules.OfType<ButtonRemapRule>()
        .Select(r => $"Remapped {r.SourceButton} -> {r.TargetButton}.").ToArray();

    private readonly string[] buttonBlockedNotes = profile.Rules.OfType<ButtonRemapRule>()
        .Select(r => $"Blocked {r.SourceButton}.").ToArray();

    /// <summary>
    /// Last-frame state of each freeze rule's activation button, for
    /// rising-edge detection. Keyed by RULE id: keyed by button, the first
    /// of two freeze rules sharing an activation button consumed the edge
    /// and the second never saw a press at all.
    /// </summary>
    private readonly Dictionary<string, bool> freezeActivationWasPressed = new(StringComparer.Ordinal);

    /// <summary>Dense array size for the ControlScriptRule bool[] round-trip -- see the Script pass in <see cref="Process"/>.</summary>
    private static readonly int buttonIdCount = Enum.GetValues<ButtonId>().Length;

    /// <summary>
    /// Scratch for that round-trip. Reused rather than allocated per script
    /// per tick: a pipeline is driven by one slot's tick loop, never two
    /// threads at once, and the engine copies the array before it runs.
    /// </summary>
    private readonly bool[] scriptButtonBuffer = new bool[buttonIdCount];

    /// <summary>
    /// Releases this pipeline's owned <see cref="LuaScriptEngine"/> (compiled
    /// scripts + their persistent Lua state tables). Callers that replace a
    /// pipeline instance -- profile switch, slot rebuild -- must call this on
    /// the OLD instance, the same discipline applied to output sinks: a
    /// replaced-without-disposed pipeline leaks every script it had loaded.
    /// </summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        luaScriptEngine.Dispose();
    }

    /// <summary>
    /// A rule's authored Enabled flag, unless a <see cref="RuleToggleRule"/>
    /// has since switched it — see <see cref="runtimeEnabledOverrides"/>.
    /// </summary>
    private bool IsEnabled(MappingRule rule) =>
        runtimeEnabledOverrides.Count == 0 || !runtimeEnabledOverrides.TryGetValue(rule.Id, out var overridden)
            ? rule.Enabled
            : overridden;

    /// <summary>
    /// Whether a rule takes part in this tick: enabled (authored or
    /// toggled), and on the right layer. A layer rule runs only while its
    /// layer is active; a Base rule runs unless the active layer binds one
    /// of the same source inputs — see <see cref="ShiftLayer"/>.
    /// </summary>
    private bool IsActive(MappingRule rule)
    {
        if (!IsEnabled(rule))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(rule.LayerId))
        {
            return rule.LayerId == shiftLayerResolver.ActiveLayerId;
        }

        return activeLayerInputs == 0
            || !baseRuleInputs.TryGetValue(rule, out var inputs)
            || (inputs & activeLayerInputs) == 0;
    }

    public ControllerFrameResult Process(ControllerSnapshot physical, DateTimeOffset now)
    {
        // Allocated on first use, not up front. Notes are a diagnostic strip
        // in the shell that only ever shows the LATEST frame, refreshed at
        // the UI rate; the overwhelming majority of ticks produce none at
        // all, and this method runs up to 1000 times a second per slot.
        List<string>? notes = null;

        // ── Buttons are four sets, combined once ──
        // passThrough: the physical buttons, after SOCD cleaning.
        // suppressed:  physical presses a rule consumes ("suppress source").
        // blocked:     physical presses a rule blocks ("Ignore / block").
        // produced:    buttons rules press (remap targets, pulses, steps…).
        // Output = (passThrough minus suppressed and blocked) OR produced.
        //
        // This used to be ONE map every pass wrote into, which is how rules
        // nullified each other: "suppress source" cleared the button for
        // everyone, so swapping South and East with two suppressing remaps
        // left only one of them pressed, a turbo on its own button either
        // held solid or never fired, and a block that happened to run after
        // a remap erased the remap's output. With separate sets, suppressing
        // or blocking only ever hides the PHYSICAL press — never a button
        // another rule (or the same rule) produces.
        var passThrough = physical.Buttons;
        var produced = ButtonMask.Empty;
        var suppressed = ButtonMask.Empty;
        var blocked = ButtonMask.Empty;

        // Shift layers resolve FIRST — every subsequent pass gates
        // through IsActive, which also checks layer membership, so this
        // must run before anything else reads it.
        var layerNote = shiftLayerResolver.Resolve(profile.ShiftLayers, physical, now);
        if (layerNote is not null)
        {
            (notes ??= []).Add(layerNote);
        }
        RefreshActiveLayerInputs();

        foreach (var layer in layersSuppressingActivator)
        {
            if (layer.ActivatorButton != ButtonId.None && physical.IsPressed(layer.ActivatorButton))
            {
                suppressed[layer.ActivatorButton] = true;
            }
            if (layer.ActivationMode == ShiftLayerActivationMode.Cycle
                && layer.CyclePreviousButton != ButtonId.None && physical.IsPressed(layer.CyclePreviousButton))
            {
                suppressed[layer.CyclePreviousButton] = true;
            }
        }

        // RuleToggleRule pass — must run before any rule whose state it
        // might flip. On the rising edge of the toggle's source button,
        // every target flips relative to its CURRENT state (authored, or
        // whatever an earlier toggle left it in).
        var toggledThisTick = false;
        foreach (var rule in ruleToggleRules)
        {
            if (rule.SourceButton == ButtonId.None) { continue; }

            var pressed = physical.IsPressed(rule.SourceButton);
            toggleSourceWasPressed.TryGetValue(rule.Id, out var wasPressed);
            // Tracked even while the toggle is inactive, so a layer engaging
            // under an already-held button doesn't read as a fresh press.
            toggleSourceWasPressed[rule.Id] = pressed;

            if (!IsActive(rule) || rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }
            if (rule.Mode == RuleMode.DoNothing)
            {
                blocked[rule.SourceButton] = true;
                continue;
            }

            if (pressed && !wasPressed)
            {
                foreach (var targetId in rule.TargetRuleIds)
                {
                    if (string.IsNullOrWhiteSpace(targetId) || !rulesById.TryGetValue(targetId, out var target))
                    {
                        continue;
                    }
                    runtimeEnabledOverrides[targetId] = !IsEnabled(target);
                }
                toggleVersion++;
                toggledThisTick = true;
                (notes ??= []).Add($"Toggled {rule.TargetRuleIds.Count} rule(s) via {rule.SourceButton}.");
            }

            if (rule.SuppressSourceButton)
            {
                suppressed[rule.SourceButton] = true;
            }
        }
        if (toggledThisTick)
        {
            // A toggle can switch a layer rule on or off, which changes
            // which Base rules that layer overrides.
            RefreshActiveLayerInputs();
        }

        // Issue #10: Use the threshold-adjusted map as the baseline for the output sticks.
        // Previously leftStick/rightStick were seeded from physical.LeftStick/RightStick,
        // which meant StickThresholdRule deadzones only affected autofire source reads,
        // not the final virtual output. Now thresholds apply to the output directly —
        // unless the threshold rule asks to suppress the stick, in which case the shaped
        // value only feeds the rules that read it as a source.
        var transformedSticks = BuildSourceStickMap(physical, out var suppressLeftStick, out var suppressRightStick);
        var leftStick = suppressLeftStick ? StickVector.Zero : transformedSticks[StickId.Left];
        var rightStick = suppressRightStick ? StickVector.Zero : transformedSticks[StickId.Right];
        var leftTrigger = physical.LeftTrigger;
        var rightTrigger = physical.RightTrigger;

        // Accumulated this tick by TouchpadMapRule's mouse mode below.
        // Unlike buttons/sticks/triggers, this has no "current state" to
        // read back — it's a relative delta for whatever consumes
        // ControllerFrameResult (the runtime's mouse writer), reset to
        // zero every tick.
        var mouseDeltaX = 0f;
        var mouseDeltaY = 0f;

        // SOCD cleaning runs first among the button passes — it's input
        // hygiene (resolving a conflicting opposite-direction pair down
        // to what the pair SHOULD report), not a creative remap, so
        // everything downstream (remaps, autofire, combos) reads the
        // cleaned result through `source` below rather than raw dual input.
        foreach (var rule in socdCleanRules)
        {
            if (!IsActive(rule))
            {
                continue;
            }

            if (rule.Mode == RuleMode.Passthrough
                || rule.NegativeButton == ButtonId.None || rule.PositiveButton == ButtonId.None)
            {
                continue;
            }
            if (rule.Mode == RuleMode.DoNothing)
            {
                blocked[rule.NegativeButton] = true;
                blocked[rule.PositiveButton] = true;
                continue;
            }

            var negNow = physical.IsPressed(rule.NegativeButton);
            var posNow = physical.IsPressed(rule.PositiveButton);
            var state = GetOrCreateSocdState(rule.Id);

            if (!negNow || !posNow)
            {
                // No conflict this tick (at most one held) — nothing to
                // clean, both pass through as-is. Reset so the NEXT
                // overlap episode re-establishes "which was first" fresh.
                state.CurrentFirst = null;
            }
            else
            {
                if (state.CurrentFirst is null)
                {
                    // Conflict just started. Whichever one was ALREADY
                    // held before this tick is "first"; a same-tick
                    // simultaneous press (both rising together) is an
                    // arbitrary but consistent tie-break toward Negative.
                    var negRising = !state.NegativeWasPressed;
                    var posRising = !state.PositiveWasPressed;
                    state.CurrentFirst = negRising && !posRising ? rule.PositiveButton
                        : posRising && !negRising ? rule.NegativeButton
                        : rule.NegativeButton;
                }

                ButtonId? winner = rule.SocdMode switch
                {
                    SocdMode.FirstWins => state.CurrentFirst,
                    SocdMode.LastWins => state.CurrentFirst == rule.NegativeButton ? rule.PositiveButton : rule.NegativeButton,
                    SocdMode.Neutral => null,
                    _ => null
                };

                if (winner != rule.NegativeButton) { passThrough[rule.NegativeButton] = false; }
                if (winner != rule.PositiveButton) { passThrough[rule.PositiveButton] = false; }
            }

            state.NegativeWasPressed = negNow;
            state.PositiveWasPressed = posNow;
        }

        // What every button-driven rule below reads: the physical frame
        // with SOCD's verdict applied. Only allocated when SOCD actually
        // changed something, which is rare.
        var source = passThrough == physical.Buttons ? physical : physical.WithButtons(passThrough);

        for (int i = 0; i < buttonRemapRules.Length; i++)
        {
            var rule = buttonRemapRules[i];
            if (!IsActive(rule))
            {
                continue;
            }

            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }

            if (rule.Mode == RuleMode.DoNothing)
            {
                blocked[rule.SourceButton] = true;
                (notes ??= []).Add(buttonBlockedNotes[i]);
                continue;
            }

            if (!source.IsPressed(rule.SourceButton))
            {
                continue;
            }

            produced[rule.TargetButton] = true;
            if (rule.SuppressSourceButton)
            {
                suppressed[rule.SourceButton] = true;
            }

            (notes ??= []).Add(buttonRemapNotes[i]);
        }

        foreach (var rule in buttonAutofireRules)
        {
            if (!IsActive(rule))
            {
                continue;
            }

            var held = source.IsPressed(rule.SourceButton);
            var scheduler = GetOrCreateBinaryScheduler(rule);
            scheduler.SetDesired(held, now);
            var pulse = scheduler.Tick(now);

            if (rule.Mode == RuleMode.DoNothing)
            {
                blocked[rule.SourceButton] = true;
                continue;
            }

            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }

            if (held)
            {
                // While its trigger is held the rule OWNS its output
                // button: the physical press of that button is hidden so
                // the release phases actually reach the game. Without
                // this, turbo on the button you are holding (the default
                // when you click a button to add a rule) just held it
                // solid. Other rules pressing the same button still OR in.
                suppressed[rule.TargetButton] = true;
            }

            if (pulse.IsPressed)
            {
                produced[rule.TargetButton] = true;
            }

            if (rule.SuppressSourceButton)
            {
                suppressed[rule.SourceButton] = true;
            }
        }

        foreach (var rule in multiButtonRules)
        {
            if (!IsActive(rule))
            {
                continue;
            }

            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }
            if (rule.Mode == RuleMode.DoNothing)
            {
                if (rule.SourceButton != ButtonId.None)
                {
                    blocked[rule.SourceButton] = true;
                }
                continue;
            }

            if (!multiButtonExecutors.TryGetValue(rule.Id, out var executor))
            {
                executor = new MultiButtonAutofireExecutor(rule);
                multiButtonExecutors[rule.Id] = executor;
            }
            executor.Apply(source, ref produced, ref suppressed, now);
        }

        // ButtonComboRule: fully built (ButtonComboExecutor.cs) since
        // before this pipeline existed, but never called — see this
        // file's history for ControlScriptRule, which had the exact
        // same problem. Positioned beside MultiButtonAutofireRule above
        // since both are button-triggered macro/combo mechanisms.
        foreach (var rule in buttonComboRules)
        {
            if (!IsActive(rule))
            {
                continue;
            }

            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }
            if (rule.Mode == RuleMode.DoNothing)
            {
                if (rule.SourceButton != ButtonId.None)
                {
                    blocked[rule.SourceButton] = true;
                }
                continue;
            }

            if (!comboExecutors.TryGetValue(rule.Id, out var comboExecutor))
            {
                comboExecutor = new ButtonComboExecutor(rule);
                comboExecutors[rule.Id] = comboExecutor;
            }

            comboExecutor.Apply(source, ref produced, ref suppressed, now);
        }

        foreach (var rule in stickAutofireRules)
        {
            if (!IsActive(rule))
            {
                continue;
            }

            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }

            if (rule.Mode == RuleMode.DoNothing)
            {
                // Block = the SOURCE stick is kept from the output (see
                // RuleMode). The target is left alone; this used to
                // centre the target stick every tick instead.
                SetStick(ref leftStick, ref rightStick, rule.SourceStick, StickVector.Zero);
                continue;
            }

            var sourceVector = transformedSticks[rule.SourceStick]
                .WithDeadzone(rule.ActivationDeadzone)
                .AmplifyToFull(rule.ActivationFullAt);
            var scheduler = GetOrCreateStickScheduler(rule);
            scheduler.SetDesired(sourceVector, now);
            var pulse = scheduler.Tick(now).Value;

            // Suppress BEFORE blending into the target. Suppressing after
            // zeroed the rule's own output whenever source and target are
            // the same stick; this way that case reads as "the pulse
            // replaces the stick", which is what suppressing it means.
            if (rule.SuppressSourceStick)
            {
                SetStick(ref leftStick, ref rightStick, rule.SourceStick, StickVector.Zero);
            }

            var current = GetStick(leftStick, rightStick, rule.TargetStick);
            var merged = ApplyBlend(current, pulse, rule.BlendMode);
            SetStick(ref leftStick, ref rightStick, rule.TargetStick, merged);
        }

        foreach (var rule in freezeRules)
        {
            if (!IsActive(rule))
            {
                continue;
            }

            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }

            if (rule.Mode == RuleMode.DoNothing)
            {
                // Block = the activation button is kept from the output
                // and nothing freezes. The target stick is left alone.
                blocked[rule.ActivationButton] = true;
                continue;
            }

            var latch = GetOrCreateFreezeLatch(rule);
            var buttonNowPressed = source.IsPressed(rule.ActivationButton);
            freezeActivationWasPressed.TryGetValue(rule.Id, out var buttonWasPressed);
            freezeActivationWasPressed[rule.Id] = buttonNowPressed;

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

                // Before the blend, for the same reason as stick autofire:
                // the default freeze captures INTO the stick it reads, and
                // suppressing afterwards wiped the frozen output.
                if (rule.SuppressCaptureStick)
                {
                    SetStick(ref leftStick, ref rightStick, rule.CaptureStick, StickVector.Zero);
                }

                var currentTarget = GetStick(leftStick, rightStick, rule.TargetStick);
                var merged = ApplyBlend(currentTarget, frozenVector, rule.BlendMode);
                SetStick(ref leftStick, ref rightStick, rule.TargetStick, merged);

                if (rule.SuppressActivationButton)
                {
                    suppressed[rule.ActivationButton] = true;
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

        // Stick Trim: "squeeze a digital trigger like it's analog."
        // Grouped with the other stick-driven, latch-style rules above.
        foreach (var rule in stickTrimRules)
        {
            if (!IsActive(rule))
            {
                continue;
            }

            if (rule.Mode == RuleMode.Passthrough || rule.ArmButton == ButtonId.None)
            {
                continue;
            }
            if (rule.Mode == RuleMode.DoNothing)
            {
                trimValues.Remove(rule.Id);
                blocked[rule.ArmButton] = true;
                continue;
            }

            var armed = source.IsPressed(rule.ArmButton);
            var current = trimValues.GetValueOrDefault(rule.Id);
            var dtSeconds = MathF.Max(0f, (float)(now - (lastTrimTickAt.TryGetValue(rule.Id, out var lastAt) ? lastAt : now)).TotalSeconds);
            lastTrimTickAt[rule.Id] = now;

            float next;
            if (armed)
            {
                // The shaped physical stick, like every other rule that
                // reads a stick as a SOURCE — not the working output, which
                // a threshold rule may have suppressed or another rule
                // already moved.
                var stickValue = transformedSticks[rule.ModulatorStick];
                var magnitude = stickValue.Magnitude;
                var target = magnitude < rule.Deadzone ? 0f
                    : Math.Clamp((magnitude - rule.Deadzone) / (1f - rule.Deadzone), 0f, 1f);

                if (rule.SuppressModulatorStick)
                {
                    SetStick(ref leftStick, ref rightStick, rule.ModulatorStick, StickVector.Zero);
                }

                // Ramp toward the target rather than jumping straight to
                // it — a fast stick flick shouldn't spike the trigger
                // instantly. RampRatePerSecond <= 0 would never move, so
                // treat it as "jump immediately" instead of dividing by
                // zero / stalling forever.
                var maxStep = rule.RampRatePerSecond > 0f ? rule.RampRatePerSecond * dtSeconds : float.MaxValue;
                next = current < target
                    ? MathF.Min(current + maxStep, target)
                    : MathF.Max(current - maxStep, target);
            }
            else
            {
                // Released: NOT a ramp in either direction — "snaps back
                // to zero the MOMENT you let go" means instant, and the
                // freeze alternative means instant too (simply don't
                // move at all). Ramping only happens while armed.
                next = rule.ResetOnRelease ? 0f : current;
            }

            trimValues[rule.Id] = next;

            // A floor under the physical trigger, never a replacement for
            // it. Assigning `next` outright made the real trigger dead
            // whenever the trim was unarmed (it wrote 0 every tick), so a
            // pad with Stick Trim on R2 could not pull R2 at all.
            if (next > 0f)
            {
                if (rule.TargetTrigger == TriggerId.Left)
                {
                    leftTrigger = MathF.Max(leftTrigger, next);
                }
                else
                {
                    rightTrigger = MathF.Max(rightTrigger, next);
                }
            }
        }

        // Gyro → stick or mouse. Runs before the multi-source rows so a
        // row can read the gyro-driven stick as one of its sources, and
        // before the touchpad pass so an explicit touchpad stick mapping
        // wins over gyro if both target the same stick (last write wins,
        // the same ordering convention every other rule here follows).
        foreach (var rule in gyroRules)
        {
            // Block has nothing to block here: motion never reaches the
            // output except through this rule (see RuleMode).
            if (!IsActive(rule) || rule.Mode is RuleMode.Passthrough or RuleMode.DoNothing)
            {
                continue;
            }

            var aim = gyroProcessor.Process(rule, source);
            if (!aim.Engaged)
            {
                // Disengaged (or no gyro on this pad): contribute nothing
                // and leave the stick to the hand. This used to write the
                // zero aim over the target stick, which made the physical
                // right stick dead whenever the gyro wasn't engaged — and
                // permanently on a pad without a gyro.
                continue;
            }

            if (rule.OutputTarget == GyroOutputTarget.Stick)
            {
                // Added to the stick, not written over it, so the thumb
                // and the gyro aim together the way gyro aiming is meant
                // to work. Re-centring on disengage is automatic: the
                // contribution is recomputed from scratch every tick.
                var current = GetStick(leftStick, rightStick, rule.TargetStick);
                SetStick(ref leftStick, ref rightStick, rule.TargetStick, (current + GyroProcessor.ToStick(aim)).Clamp());
            }
            else
            {
                // First tick has no previous timestamp to measure
                // against — a zero delta means no movement rather than
                // the clamp ceiling, which would jump the cursor on the
                // very first frame. Same guard as the touchpad's
                // first-contact case.
                var deltaSeconds = lastGyroTickAt == DateTimeOffset.MinValue
                    ? 0f
                    : (float)Math.Clamp((now - lastGyroTickAt).TotalSeconds, 0d, 0.1d);
                var (dx, dy) = GyroProcessor.ToMouseDelta(aim, deltaSeconds);
                mouseDeltaX += dx;
                mouseDeltaY += dy;
            }
        }
        lastGyroTickAt = now;

        // Touchpad → stick anchor / wedge D-pad. See TouchpadMapRule.cs
        // for why mouse X/Y isn't here.
        foreach (var rule in touchpadRules)
        {
            if (!IsActive(rule))
            {
                // A rule switched off mid-stroke must not resume that
                // stroke when it comes back — its start timestamp would
                // by then be arbitrarily old, and the finger still resting
                // on the pad would classify as an enormous long press.
                if (touchGestureEngines.TryGetValue(rule.Id, out var idleEngine))
                {
                    idleEngine.Reset();
                }
                continue;
            }

            if (rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }

            ProcessGestures(rule, physical, ref produced, now);

            var state = GetOrCreateTouchAnchor(rule.Id);
            var down = rule.Mode != RuleMode.DoNothing && physical.TouchDown;

            if (down && !state.WasDown)
            {
                state.AnchorX = physical.TouchX;
                state.AnchorY = physical.TouchY;
            }

            if (down)
            {
                var dx = physical.TouchX - state.AnchorX;
                // Touch Y follows SDL's screen convention (0 = top); this
                // codebase's stick Y is up-positive (see the real stick
                // read a few hundred lines up: `-NormalizeSignedAxis(...Y)`)
                // — negate here to match that same convention.
                var dy = -(physical.TouchY - state.AnchorY);

                if (rule.StickEnabled)
                {
                    var stick = new StickVector(
                        Math.Clamp(dx * rule.StickSensitivity, -1f, 1f),
                        Math.Clamp(dy * rule.StickSensitivity, -1f, 1f));
                    SetStick(ref leftStick, ref rightStick, rule.TargetStick, stick);
                }

                if (rule.DpadEnabled)
                {
                    ApplyWedgeDpad(dx, dy, rule.DpadDeadzoneRadius, rule.DpadEightWay, ref produced);
                }

                if (rule.MouseEnabled && state.WasDown)
                {
                    // Frame-to-frame, NOT anchor-relative — how a laptop
                    // touchpad actually works: each tick's finger
                    // movement nudges the cursor, it doesn't matter
                    // where the finger originally landed. Gated on
                    // state.WasDown (not just `down`) so the FIRST
                    // contact tick contributes no delta — there is no
                    // meaningful "previous position" to diff against yet,
                    // and without this gate that tick would read as a
                    // teleport from (0,0) to wherever the finger landed.
                    var mdx = physical.TouchX - state.PrevMouseX;
                    // Touch Y and screen Y both grow downward already —
                    // UNLIKE the stick/D-pad negation above, no flip
                    // needed here; a finger dragged down should move the
                    // cursor down.
                    var mdy = physical.TouchY - state.PrevMouseY;

                    mouseDeltaX += mdx * rule.MouseSensitivityX * MouseDeltaReferencePixels * (rule.InvertMouseX ? -1f : 1f);
                    mouseDeltaY += mdy * rule.MouseSensitivityY * MouseDeltaReferencePixels * (rule.InvertMouseY ? -1f : 1f);
                }

                state.PrevMouseX = physical.TouchX;
                state.PrevMouseY = physical.TouchY;
            }

            // No finger, no output: nothing is written, so the physical
            // stick and D-pad pass straight through. The rule used to
            // centre its stick and release all four D-pad buttons here on
            // every untouched tick — see TouchpadMapRule.

            state.WasDown = down;
        }

        // Re-assert every gesture-driven button that hasn't expired.
        // Done AFTER the touchpad rules and BEFORE multi-source rows and
        // scripts, so both can read gesture-driven buttons as ordinary
        // sources. A gesture bound to a D-pad direction still wins over
        // the wedge D-pad for the length of its pulse: both only ever
        // ADD presses now, so nothing the wedge does can cut it short.
        ApplyGestureHolds(ref produced, now);

        // Multi-source mapping rows: many inputs, one output, a combine
        // mode (or formula) deciding how they fold together. Sources
        // read from the ACCUMULATED working state — buttons the remap
        // passes already produced, sticks/triggers as shaped so far —
        // so rows compose with everything above; scripts (below) still
        // get the final say. A row OWNS its target for the tick: below
        // threshold releases a button target even if it's physically
        // held, which is what makes "these sources and nothing else"
        // mappings possible. Ownership is its own pair of sets, so a row
        // overrides its target without the suppress/produce bookkeeping
        // above losing track of anyone else's buttons.
        var owned = ButtonMask.Empty;
        var ownedPressed = ButtonMask.Empty;
        foreach (var rule in multiSourceRules)
        {
            if (!IsActive(rule))
            {
                continue;
            }

            if (rule.Mode == RuleMode.Passthrough || rule.Sources.Count == 0)
            {
                continue;
            }
            if (rule.Mode == RuleMode.DoNothing)
            {
                // Block = every source is kept from the output; the
                // target is not written (see RuleMode).
                foreach (var mapSource in rule.Sources)
                {
                    SuppressMapSource(mapSource, ref blocked, ref leftStick, ref rightStick, ref leftTrigger, ref rightTrigger);
                }
                continue;
            }

            var working = ComposeButtons(passThrough, suppressed, blocked, produced, owned, ownedPressed);
            var values = new float[rule.Sources.Count];
            for (var i = 0; i < rule.Sources.Count; i++)
            {
                values[i] = ReadMapSource(rule.Sources[i], working, leftStick, rightStick, leftTrigger, rightTrigger, physical);
            }

            float combined;
            if (rule.CombineMode == CombineMode.Formula)
            {
                if (!compiledFormulas.TryGetValue(rule.Id, out var evaluator))
                {
                    evaluator = Formulas.FormulaExpression.Compile(rule.Formula, rule.Sources.Count, out var formulaError);
                    compiledFormulas[rule.Id] = evaluator;
                    if (evaluator is null)
                    {
                        (notes ??= []).Add($"Formula in '{rule.Name}' failed to compile: {formulaError}");
                    }
                }
                if (evaluator is null)
                {
                    continue; // compile failed — noted once above, rule inert until the profile is edited (which rebuilds this pipeline)
                }
                combined = evaluator(values);
            }
            else
            {
                combined = CombineValues(rule.CombineMode, values);
            }

            if (rule.SuppressSources)
            {
                foreach (var mapSource in rule.Sources)
                {
                    SuppressMapSource(mapSource, ref suppressed, ref leftStick, ref rightStick, ref leftTrigger, ref rightTrigger);
                }
            }

            WriteMapTarget(rule, combined, ref leftStick, ref rightStick, ref leftTrigger, ref rightTrigger, ref owned, ref ownedPressed);
        }

        // Scripts get the final say over buttons. Their own suppression
        // and blocking go into the sets FIRST, so the map a script is
        // handed already hides its source — and a script that presses its
        // own source button (or one another rule produced) keeps it.
        // Applying the suppression after the script ran wiped both.
        var anyScriptRuns = false;
        foreach (var rule in controlScriptRules)
        {
            if (!IsActive(rule) || rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }

            // SuppressSourceInput: the doc comment on ControlScriptRule
            // flags this as "future-facing" with no fixed semantics yet.
            // The one unambiguous case is ControlKey naming a button
            // outright ("South") -- suppress that source button the same
            // way every other rule's SuppressSourceButton does. A
            // dotted key ("LeftStick.Button", "RightTrigger.Analog")
            // names a stick or trigger; deliberately NOT guessing which
            // axis/sub-control to zero there, since a wrong guess would
            // silently kill legitimate output rather than just no-op.
            var namesButton = Enum.TryParse<ButtonId>(rule.ControlKey, ignoreCase: true, out var sourceButton);
            if (rule.Mode == RuleMode.DoNothing)
            {
                if (namesButton)
                {
                    blocked[sourceButton] = true;
                }
                continue;
            }

            if (string.IsNullOrWhiteSpace(rule.ScriptCode))
            {
                continue;
            }

            anyScriptRuns = true;
            if (rule.SuppressSourceInput && namesButton)
            {
                suppressed[sourceButton] = true;
            }
        }

        var buttons = ComposeButtons(passThrough, suppressed, blocked, produced, owned, ownedPressed);

        if (anyScriptRuns)
        {
            // LuaScriptEngine wants a dense bool[] indexed by (int)ButtonId
            // (its press()/release() callbacks index it directly); the
            // rest of this method carries button state as a ButtonMask.
            // One round-trip covers every script, in rule order, each
            // seeing what the ones before it did.
            var buttonArray = scriptButtonBuffer;
            Array.Clear(buttonArray);
            foreach (var id in buttons)
            {
                buttonArray[(int)id] = true;
            }

            foreach (var rule in controlScriptRules)
            {
                if (!IsActive(rule) || rule.Mode != RuleMode.Modify || string.IsNullOrWhiteSpace(rule.ScriptCode))
                {
                    continue;
                }

                // virtualBefore isn't read by Execute today (checked against
                // the engine's own source); passing `physical` rather than a
                // null-forgiving placeholder keeps this call honest if that
                // ever changes.
                luaScriptEngine.Execute(
                    rule, physical, virtualBefore: physical, buttonArray,
                    ref leftStick, ref rightStick, ref leftTrigger, ref rightTrigger, now);
            }

            for (var i = 0; i < buttonArray.Length; i++)
            {
                buttons[(ButtonId)i] = buttonArray[i];
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

        // Axis inversion is the LAST thing that happens, in a pass of its
        // own after every rule — see AxisInvertProcessor for why it sits
        // behind the rules rather than in front of them. When the
        // resolved inversion doesn't ask the layout to show it, the two
        // snapshots diverge here and only here: the sink gets `inverted`,
        // the panels keep `virtualSnapshot`.
        var invert = ResolveAxisInvert();
        var inverted = AxisInvertProcessor.Apply(virtualSnapshot, invert);
        var showInLayout = invert?.ShowInLayout ?? false;

        return new ControllerFrameResult(
            physical with { Timestamp = now },
            showInLayout ? inverted : virtualSnapshot,
            (IReadOnlyList<string>?)notes ?? [])
        {
            ActiveLayerId = shiftLayerResolver.ActiveLayerId,
            MouseDeltaX = mouseDeltaX,
            MouseDeltaY = mouseDeltaY,
            OutputSnapshot = inverted
        };
    }

    /// <summary>
    /// The button output as the four sets of <see cref="Process"/> plus a
    /// multi-source row's ownership describe it: physical presses nobody
    /// suppressed or blocked, OR every produced press — and then each
    /// owned button forced to whatever its row decided.
    /// </summary>
    private static ButtonMask ComposeButtons(
        ButtonMask passThrough, ButtonMask suppressed, ButtonMask blocked, ButtonMask produced,
        ButtonMask owned, ButtonMask ownedPressed) =>
        (passThrough.Without(suppressed | blocked) | produced).Without(owned) | ownedPressed;

    /// <summary>
    /// Folds every active <see cref="AxisInvertRule"/> into one inversion:
    /// Base rules first, then the active layer's, each group in list order,
    /// with <see cref="AxisInvert.Overlay"/> — so a later opinion about an
    /// axis wins and "no opinion" defers. Null when no rule applies.
    ///
    /// <para>
    /// Cached against the active layer and <see cref="toggleVersion"/>,
    /// the only two things that can change the answer during this
    /// pipeline's life: without the cache every tick would allocate a
    /// fresh record per rule, at up to 1000 Hz per slot, to recompute a
    /// value that almost never changes.
    /// </para>
    /// </summary>
    private AxisInvert? ResolveAxisInvert()
    {
        if (axisInvertRules.Length == 0)
        {
            return null;
        }

        var layerId = shiftLayerResolver.ActiveLayerId;
        if (resolvedAxisInvertToggleVersion == toggleVersion
            && string.Equals(resolvedAxisInvertLayerId, layerId, StringComparison.Ordinal))
        {
            return resolvedAxisInvert;
        }

        AxisInvert? folded = null;
        foreach (var rule in axisInvertRules)
        {
            if (string.IsNullOrEmpty(rule.LayerId) && IsActive(rule) && rule.Mode == RuleMode.Modify)
            {
                folded = (folded ?? AxisInvert.None).Overlay(rule.Axes);
            }
        }
        if (!string.IsNullOrEmpty(layerId))
        {
            foreach (var rule in axisInvertRules)
            {
                if (rule.LayerId == layerId && IsActive(rule) && rule.Mode == RuleMode.Modify)
                {
                    folded = (folded ?? AxisInvert.None).Overlay(rule.Axes);
                }
            }
        }

        resolvedAxisInvert = folded;
        resolvedAxisInvertLayerId = layerId;
        resolvedAxisInvertToggleVersion = toggleVersion;
        return folded;
    }

    /// <summary>
    /// Recomputes <see cref="activeLayerInputs"/> from the active layer's
    /// rules that are currently enabled. A layer rule that is switched off
    /// does not take its input away from Base; one switched on by a toggle
    /// does, from that tick on. Its <see cref="RuleMode"/> does not matter:
    /// a Passthrough rule in a layer is how a layer says "this button is
    /// plain again here".
    /// </summary>
    private void RefreshActiveLayerInputs()
    {
        activeLayerInputs = 0;
        var layerId = shiftLayerResolver.ActiveLayerId;
        if (string.IsNullOrEmpty(layerId) || !layerRuleInputs.TryGetValue(layerId, out var entries))
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (IsEnabled(entry.Rule))
            {
                activeLayerInputs |= entry.Inputs;
            }
        }
    }

    /// <summary>One layer rule and its source inputs, for <see cref="RefreshActiveLayerInputs"/>.</summary>
    private readonly record struct LayerRuleInputs(MappingRule Rule, ulong Inputs);

    private static Dictionary<string, LayerRuleInputs[]> BuildLayerRuleInputs(IReadOnlyList<MappingRule> rules) =>
        rules
            .Where(rule => !string.IsNullOrEmpty(rule.LayerId))
            .Select(rule => new LayerRuleInputs(rule, SourceInputs(rule)))
            .Where(entry => entry.Inputs != 0)
            .GroupBy(entry => entry.Rule.LayerId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

    private static Dictionary<MappingRule, ulong> BuildBaseRuleInputs(IReadOnlyList<MappingRule> rules)
    {
        var map = new Dictionary<MappingRule, ulong>(ReferenceEqualityComparer.Instance);
        foreach (var rule in rules)
        {
            if (string.IsNullOrEmpty(rule.LayerId))
            {
                var inputs = SourceInputs(rule);
                if (inputs != 0)
                {
                    map[rule] = inputs;
                }
            }
        }
        return map;
    }

    private static Dictionary<string, MappingRule> BuildRulesById(IReadOnlyList<MappingRule> rules)
    {
        var map = new Dictionary<string, MappingRule>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            map.TryAdd(rule.Id, rule);
        }
        return map;
    }

    // ── Source inputs as bits ──
    // Buttons use their own ButtonMask bit (0..31, which ButtonMask's
    // static guard keeps true); sticks, triggers, touchpad and gyro take
    // the bits above. None is never an input.
    private const int StickInputShift = 32;
    private const int TriggerInputShift = 34;
    private const ulong TouchpadInput = 1UL << 36;
    private const ulong GyroInput = 1UL << 37;

    private static ulong InputBit(ButtonId button) =>
        button == ButtonId.None || (uint)button >= 32u ? 0UL : 1UL << (int)button;

    private static ulong InputBit(StickId stick) => 1UL << (StickInputShift + (int)stick);

    private static ulong InputBit(TriggerId trigger) => 1UL << (TriggerInputShift + (int)trigger);

    /// <summary>
    /// The inputs a rule READS — what a layer rule takes over from Base.
    /// Targets do not count: two rules that write the same output but
    /// listen to different inputs are not overriding each other. Axis
    /// inversion reads nothing (it folds per axis instead).
    /// </summary>
    private static ulong SourceInputs(MappingRule rule) => rule switch
    {
        ButtonRemapRule r => InputBit(r.SourceButton),
        ButtonAutofireRule r => InputBit(r.SourceButton),
        MultiButtonAutofireRule r => InputBit(r.SourceButton),
        ButtonComboRule r => InputBit(r.SourceButton),
        RuleToggleRule r => InputBit(r.SourceButton),
        SocdCleanRule r => InputBit(r.NegativeButton) | InputBit(r.PositiveButton),
        StickThresholdRule r => InputBit(r.TargetStick),
        StickAutofireRule r => InputBit(r.SourceStick),
        FreezeLastDirectionRule r => InputBit(r.ActivationButton) | InputBit(r.CaptureStick),
        StickTrimRule r => InputBit(r.ArmButton) | InputBit(r.ModulatorStick),
        GyroMapRule r => GyroInput | InputBit(r.EngageButton),
        TouchpadMapRule => TouchpadInput,
        MultiSourceMapRule r => r.Sources.Aggregate(0UL, (bits, source) => bits | SourceInputs(source)),
        ControlScriptRule r => ControlKeyInputs(r.ControlKey),
        _ => 0UL
    };

    private static ulong SourceInputs(MapSource source) => source.Kind switch
    {
        MapSourceKind.Button => InputBit(source.Button),
        MapSourceKind.StickAxisX or MapSourceKind.StickAxisY or MapSourceKind.StickMagnitude => InputBit(source.Stick),
        MapSourceKind.Trigger => InputBit(source.Trigger),
        MapSourceKind.GyroPitch or MapSourceKind.GyroYaw or MapSourceKind.GyroRoll => GyroInput,
        _ => 0UL
    };

    /// <summary>
    /// A script's control key ("South", "LeftStick.Button", "RightStick",
    /// "LeftTrigger.Analog") as an input. Unrecognised keys bind nothing
    /// rather than guessing.
    /// </summary>
    private static ulong ControlKeyInputs(string? controlKey)
    {
        if (string.IsNullOrWhiteSpace(controlKey))
        {
            return 0UL;
        }

        var key = controlKey.Trim();
        var dot = key.IndexOf('.');
        var head = dot < 0 ? key : key[..dot];
        var tail = dot < 0 ? string.Empty : key[(dot + 1)..];
        var isButtonSuffix = tail.Equals("Button", StringComparison.OrdinalIgnoreCase);

        if (head.Equals("LeftStick", StringComparison.OrdinalIgnoreCase))
        {
            return isButtonSuffix ? InputBit(ButtonId.LeftStick) : InputBit(StickId.Left);
        }
        if (head.Equals("RightStick", StringComparison.OrdinalIgnoreCase))
        {
            return isButtonSuffix ? InputBit(ButtonId.RightStick) : InputBit(StickId.Right);
        }
        if (head.Equals("LeftTrigger", StringComparison.OrdinalIgnoreCase))
        {
            return isButtonSuffix ? InputBit(ButtonId.LeftTriggerButton) : InputBit(TriggerId.Left);
        }
        if (head.Equals("RightTrigger", StringComparison.OrdinalIgnoreCase))
        {
            return isButtonSuffix ? InputBit(ButtonId.RightTriggerButton) : InputBit(TriggerId.Right);
        }
        if (head.Equals("Touchpad", StringComparison.OrdinalIgnoreCase) && dot >= 0)
        {
            return TouchpadInput;
        }

        return Enum.TryParse<ButtonId>(head, ignoreCase: true, out var button) ? InputBit(button) : 0UL;
    }

    /// <summary>
    /// The two sticks after <see cref="StickThresholdRule"/> conditioning,
    /// indexable by <see cref="StickId"/>.
    ///
    /// <para>
    /// A struct rather than a <see cref="Dictionary{TKey,TValue}"/> because
    /// <see cref="StickId"/> has exactly two members and this is rebuilt on
    /// every tick of every slot: a two-entry dictionary meant a heap
    /// allocation plus hashing on the hottest path in the app, to hold
    /// what fits in two fields.
    /// </para>
    /// </summary>
    private struct SourceStickMap(StickVector left, StickVector right)
    {
        private StickVector left = left;
        private StickVector right = right;

        public StickVector this[StickId stickId]
        {
            readonly get => stickId == StickId.Left ? left : right;
            set
            {
                if (stickId == StickId.Left) { left = value; } else { right = value; }
            }
        }
    }

    /// <summary>
    /// Builds a threshold-adjusted stick map. This map now also seeds the baseline
    /// output stick values (leftStick, rightStick) so that StickThresholdRule
    /// deadzones and amplification are reflected in the final virtual output.
    /// <paramref name="suppressLeft"/>/<paramref name="suppressRight"/> report a
    /// threshold rule's <see cref="StickThresholdRule.SuppressSourceStick"/>: the
    /// map keeps the shaped value for rules that read it as a source, and the
    /// caller starts that stick's OUTPUT from centre.
    /// </summary>
    private SourceStickMap BuildSourceStickMap(ControllerSnapshot snapshot, out bool suppressLeft, out bool suppressRight)
    {
        var map = new SourceStickMap(snapshot.LeftStick, snapshot.RightStick);
        suppressLeft = false;
        suppressRight = false;

        foreach (var rule in stickThresholdRules)
        {
            if (!IsActive(rule) || rule.Mode == RuleMode.Passthrough)
            {
                continue;
            }

            if (rule.Mode == RuleMode.DoNothing)
            {
                map[rule.TargetStick] = StickVector.Zero;
                continue;
            }

            map[rule.TargetStick] = map[rule.TargetStick]
                .WithDeadzone(rule.Deadzone)
                .AmplifyToFull(rule.FullAt);

            if (rule.SuppressSourceStick)
            {
                if (rule.TargetStick == StickId.Left) { suppressLeft = true; } else { suppressRight = true; }
            }
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

    /// <summary>Reads one <see cref="MapSource"/> as a float from the accumulated working state. Button 0/1, trigger 0..1, axis -1..1, magnitude 0..1; Invert flips (1-v unsigned, -v signed).</summary>
    private static float ReadMapSource(
        MapSource source, in ButtonMask buttons,
        StickVector leftStick, StickVector rightStick, float leftTrigger, float rightTrigger,
        ControllerSnapshot physical)
    {
        var value = source.Kind switch
        {
            MapSourceKind.Button => buttons[source.Button] ? 1f : 0f,
            MapSourceKind.StickAxisX => GetStick(leftStick, rightStick, source.Stick).X,
            MapSourceKind.StickAxisY => GetStick(leftStick, rightStick, source.Stick).Y,
            MapSourceKind.StickMagnitude => GetStick(leftStick, rightStick, source.Stick).Magnitude,
            MapSourceKind.Trigger => source.Trigger == TriggerId.Left ? leftTrigger : rightTrigger,
            // Gyro is read from the physical snapshot rather than the
            // accumulated working state: no earlier pass writes angular
            // velocity, so there's nothing accumulated to read.
            MapSourceKind.GyroPitch => physical.GyroPitch,
            MapSourceKind.GyroYaw => physical.GyroYaw,
            MapSourceKind.GyroRoll => physical.GyroRoll,
            _ => 0f
        };

        if (!source.Invert)
        {
            return value;
        }
        // Signed sources mirror around zero; unsigned ones (button,
        // trigger, magnitude — all bounded to 0..1) flip within that
        // range. Gyro belongs with the SIGNED group: angular velocity is
        // signed AND unbounded, so "1 - v" would be meaningless for it.
        return source.Kind is MapSourceKind.StickAxisX or MapSourceKind.StickAxisY
            or MapSourceKind.GyroPitch or MapSourceKind.GyroYaw or MapSourceKind.GyroRoll
            ? -value
            : 1f - value;
    }

    /// <summary>Folds the source values per the (non-Formula) combine mode.</summary>
    private static float CombineValues(CombineMode mode, float[] values)
    {
        const float FirstActiveEpsilon = 0.01f;
        switch (mode)
        {
            case CombineMode.Maximum:
                {
                    var best = values[0];
                    for (var i = 1; i < values.Length; i++) { best = MathF.Max(best, values[i]); }
                    return best;
                }
            case CombineMode.Minimum:
                {
                    var worst = values[0];
                    for (var i = 1; i < values.Length; i++) { worst = MathF.Min(worst, values[i]); }
                    return worst;
                }
            case CombineMode.Sum:
                {
                    var sum = 0f;
                    foreach (var v in values) { sum += v; }
                    return sum;
                }
            case CombineMode.Average:
                {
                    var sum = 0f;
                    foreach (var v in values) { sum += v; }
                    return sum / values.Length;
                }
            case CombineMode.Multiply:
                {
                    var product = 1f;
                    foreach (var v in values) { product *= v; }
                    return product;
                }
            case CombineMode.FirstActive:
                {
                    foreach (var v in values)
                    {
                        if (MathF.Abs(v) > FirstActiveEpsilon) { return v; }
                    }
                    return 0f;
                }
            default:
                return 0f;
        }
    }

    /// <summary>
    /// Zeroes one source's own contribution in the virtual output. A
    /// button source goes into <paramref name="hidden"/> (the suppressed or
    /// blocked set), which hides only its PHYSICAL press — clearing it in
    /// the shared map, as this used to, also released the button for any
    /// other rule that had pressed it.
    /// </summary>
    private static void SuppressMapSource(
        MapSource source, ref ButtonMask hidden,
        ref StickVector leftStick, ref StickVector rightStick, ref float leftTrigger, ref float rightTrigger)
    {
        switch (source.Kind)
        {
            case MapSourceKind.Button:
                if (source.Button != ButtonId.None) { hidden[source.Button] = true; }
                break;
            case MapSourceKind.StickAxisX:
                {
                    var stick = GetStick(leftStick, rightStick, source.Stick) with { X = 0f };
                    SetStick(ref leftStick, ref rightStick, source.Stick, stick);
                    break;
                }
            case MapSourceKind.StickAxisY:
                {
                    var stick = GetStick(leftStick, rightStick, source.Stick) with { Y = 0f };
                    SetStick(ref leftStick, ref rightStick, source.Stick, stick);
                    break;
                }
            case MapSourceKind.StickMagnitude:
                SetStick(ref leftStick, ref rightStick, source.Stick, StickVector.Zero);
                break;
            case MapSourceKind.Trigger:
                if (source.Trigger == TriggerId.Left) { leftTrigger = 0f; } else { rightTrigger = 0f; }
                break;

                // Gyro axes are deliberately absent: they're read straight
                // from the physical snapshot, and nothing in the virtual
                // output carries angular velocity, so there is no
                // contribution to suppress. Falling through is correct here,
                // not a missing case.
        }
    }

    /// <summary>
    /// Writes the combined value to the row's target — button via threshold
    /// (the row takes ownership of it: <paramref name="owned"/> marks it,
    /// <paramref name="ownedPressed"/> holds the verdict), axis preserving
    /// its sibling axis, trigger clamped.
    /// </summary>
    private static void WriteMapTarget(
        MultiSourceMapRule rule, float combined,
        ref StickVector leftStick, ref StickVector rightStick, ref float leftTrigger, ref float rightTrigger,
        ref ButtonMask owned, ref ButtonMask ownedPressed)
    {
        switch (rule.TargetKind)
        {
            case MapTargetKind.Button:
                if (rule.TargetButton != ButtonId.None)
                {
                    owned[rule.TargetButton] = true;
                    ownedPressed[rule.TargetButton] = combined >= rule.PressThreshold;
                }
                break;
            case MapTargetKind.StickAxisX:
                {
                    var stick = GetStick(leftStick, rightStick, rule.TargetStick) with { X = Math.Clamp(combined, -1f, 1f) };
                    SetStick(ref leftStick, ref rightStick, rule.TargetStick, stick);
                    break;
                }
            case MapTargetKind.StickAxisY:
                {
                    var stick = GetStick(leftStick, rightStick, rule.TargetStick) with { Y = Math.Clamp(combined, -1f, 1f) };
                    SetStick(ref leftStick, ref rightStick, rule.TargetStick, stick);
                    break;
                }
            case MapTargetKind.Trigger:
                var clamped = Math.Clamp(combined, 0f, 1f);
                if (rule.TargetTrigger == TriggerId.Left) { leftTrigger = clamped; } else { rightTrigger = clamped; }
                break;
        }
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
