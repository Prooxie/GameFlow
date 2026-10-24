using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;

namespace GameFlow.Core.Pipeline;

/// <summary>
/// Per-rule executor for a <see cref="MultiButtonAutofireRule"/>.
///
/// <para>
/// State tracked here:
/// <list type="bullet">
///   <item><b>activationStart</b> — the timestamp at which the source
///         transitioned from released to pressed. Set on rising edge;
///         cleared on falling edge so the next press restarts the
///         timeline at step 0.</item>
///   <item><b>sourceWasPressed</b> — last frame's state of the source
///         button, used for edge detection.</item>
/// </list>
/// </para>
///
/// <para>
/// On every <see cref="Apply"/> call we:
/// <list type="number">
///   <item>Detect rising / falling edges of the source.</item>
///   <item>If the source is currently held, compute
///         <c>elapsed = (now - activationStart) mod cycleDuration</c>,
///         find the step whose [holdStart, holdEnd) window contains
///         that elapsed value, and press its target button.</item>
///   <item>If <see cref="MultiButtonAutofireRule.SuppressSourceButton"/>
///         is set, add the source to the suppressed set.</item>
/// </list>
/// </para>
///
/// <para>
/// Like <see cref="ButtonComboExecutor"/>, this uses "any-pressed
/// wins" semantics — it only ever adds to the produced set and never
/// clears a button, so overlapping rules compose cleanly. Suppression is
/// a separate set for the same reason: it only hides the PHYSICAL press
/// of the source, so it can neither erase another rule's output on that
/// button nor this rule's own step when a step targets its own source.
/// </para>
/// </summary>
public sealed class MultiButtonAutofireExecutor
{
    private readonly MultiButtonAutofireRule rule;
    private DateTimeOffset? activationStart;
    private bool sourceWasPressed;

    public MultiButtonAutofireExecutor(MultiButtonAutofireRule rule)
    {
        this.rule = rule;
    }

    /// <param name="source">The input this tick, after SOCD cleaning.</param>
    /// <param name="produced">Buttons rules are pressing this tick; a step's target is added here.</param>
    /// <param name="suppressed">Physical presses to hide from the output; the source is added here when the rule suppresses it.</param>
    /// <param name="now">Current tick timestamp.</param>
    public void Apply(ControllerSnapshot source, ref ButtonMask produced, ref ButtonMask suppressed, DateTimeOffset now)
    {
        if (rule.SourceButton == ButtonId.None || rule.Steps.Count == 0)
        {
            return;
        }

        var sourcePressed = source.IsPressed(rule.SourceButton);

        // Rising edge — anchor the timeline.
        if (sourcePressed && !sourceWasPressed)
        {
            activationStart = now;
        }
        // Falling edge — stop immediately, no carry-over.
        if (!sourcePressed && sourceWasPressed)
        {
            activationStart = null;
        }
        sourceWasPressed = sourcePressed;

        if (rule.SuppressSourceButton)
        {
            suppressed[rule.SourceButton] = true;
        }

        if (activationStart is null)
        {
            return;
        }

        // Cycle duration = sum of every step's HoldMs + ReleaseMs +
        // DelayAfterMs. A zero total means the user authored a
        // degenerate ruleset; bail out rather than divide-by-zero.
        var cycle = 0;
        foreach (var step in rule.Steps)
        {
            cycle += Math.Max(0, step.HoldMs)
                  +  Math.Max(0, step.ReleaseMs)
                  +  Math.Max(0, step.DelayAfterMs);
        }
        if (cycle <= 0)
        {
            return;
        }

        var elapsed = (int)((now - activationStart.Value).TotalMilliseconds);
        // Clamp to a sane positive value before modulo to avoid
        // negative-elapsed weirdness if the clock skews backwards.
        if (elapsed < 0) { elapsed = 0; }
        var position = elapsed % cycle;

        // Walk steps and find which one's hold window contains 'position'.
        var cursor = 0;
        foreach (var step in rule.Steps)
        {
            var hold    = Math.Max(0, step.HoldMs);
            var release = Math.Max(0, step.ReleaseMs);
            var delay   = Math.Max(0, step.DelayAfterMs);
            var stepLen = hold + release + delay;

            // Only the [cursor, cursor + hold) sub-window emits a press
            // — release and delay are silent.
            if (position >= cursor && position < cursor + hold
                && step.TargetButton != ButtonId.None)
            {
                produced[step.TargetButton] = true;
                return;
            }
            cursor += stepLen;
        }
    }

    /// <summary>Resets timeline state. Called when rules are swapped out.</summary>
    public void Reset()
    {
        activationStart = null;
        sourceWasPressed = false;
    }
}
