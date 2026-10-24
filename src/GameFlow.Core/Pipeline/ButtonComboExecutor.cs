using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;

namespace GameFlow.Core.Pipeline;

/// <summary>
/// Stateful executor for a single <see cref="ButtonComboRule"/>.
///
/// Tracks the rising-edge timestamp of the source button, then on each tick
/// applies the slice of the timeline that's active right now. Multiple combos
/// may overlap on the same virtual button: when that happens the executor uses
/// "any-pressed wins" — i.e. if two concurrent combos both want South pressed,
/// it stays pressed for the union of their windows.
///
/// <para>
/// Steps go into the pipeline's PRODUCED set and source suppression into
/// its SUPPRESSED set, never into one shared button map. With a single map,
/// clearing the suppressed source also cleared that button for every other
/// rule that had pressed it — including this combo's own step when a step
/// targets its own source.
/// </para>
/// </summary>
public sealed class ButtonComboExecutor
{
    private readonly ButtonComboRule rule;
    private DateTimeOffset? activationStart;
    private bool sourceWasPressed;

    public ButtonComboExecutor(ButtonComboRule rule)
    {
        this.rule = rule;
    }

    /// <summary>
    /// Adds the steps active at <paramref name="now"/> to
    /// <paramref name="produced"/>, and the source to
    /// <paramref name="suppressed"/> when the rule suppresses it.
    /// </summary>
    /// <param name="source">The input this tick, after SOCD cleaning.</param>
    /// <param name="produced">Buttons rules are pressing this tick.</param>
    /// <param name="suppressed">Physical presses to hide from the output.</param>
    /// <param name="now">Current tick timestamp.</param>
    public void Apply(ControllerSnapshot source, ref ButtonMask produced, ref ButtonMask suppressed, DateTimeOffset now)
    {
        if (rule.SourceButton == ButtonId.None || rule.Steps.Count == 0)
        {
            return;
        }

        var sourcePressed = source.IsPressed(rule.SourceButton);

        // Rising edge — start the combo timeline.
        if (sourcePressed && !sourceWasPressed)
        {
            activationStart = now;
        }

        // Falling edge — abort unfinished steps unless PlayToCompletion is set.
        if (!sourcePressed && sourceWasPressed && !rule.PlayToCompletion)
        {
            activationStart = null;
        }

        sourceWasPressed = sourcePressed;

        if (activationStart is null)
        {
            // Nothing playing, but a suppressed source stays hidden either way.
            if (rule.SuppressSourceButton)
            {
                suppressed[rule.SourceButton] = true;
            }
            return;
        }

        var elapsed = (int)(now - activationStart.Value).TotalMilliseconds;
        var totalDuration = 0;
        var anyStepActive = false;

        var cumulativeOffset = 0;
        foreach (var step in rule.Steps)
        {
            var startMs = step.PreDelayMs > 0
                ? step.PreDelayMs
                : cumulativeOffset;

            var holdMs  = step.HoldMs > 0 ? step.HoldMs : 60;
            var endMs   = startMs + holdMs;

            if (elapsed >= startMs && elapsed < endMs && step.Button != ButtonId.None)
            {
                // Latch (any-pressed wins) — never clear a button that another rule may want.
                produced[step.Button] = true;
                anyStepActive = true;
            }

            cumulativeOffset = endMs + rule.InterStepGapMs;
            totalDuration = Math.Max(totalDuration, endMs);
        }

        // If we've passed the end of the timeline, deactivate the combo so the
        // source can re-trigger it.
        if (elapsed >= totalDuration)
        {
            activationStart = null;
        }

        if (rule.SuppressSourceButton)
        {
            suppressed[rule.SourceButton] = true;
        }

        // Mark elapsed used so callers can ignore noise about unused locals if compiled with warnings-as-errors.
        _ = anyStepActive;
    }

    public void Reset()
    {
        activationStart = null;
        sourceWasPressed = false;
    }
}
