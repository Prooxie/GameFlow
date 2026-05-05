using Autofire.Core.Enums;
using Autofire.Core.Models;
using Autofire.Core.Models.Rules;

namespace Autofire.Core.Pipeline;

/// <summary>
/// Stateful executor for a single <see cref="ButtonComboRule"/>.
///
/// Tracks the rising-edge timestamp of the source button, then on each tick
/// applies the slice of the timeline that's active right now. Multiple combos
/// may overlap on the same virtual button: when that happens the executor uses
/// "any-pressed wins" — i.e. if two concurrent combos both want South pressed,
/// it stays pressed for the union of their windows.
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
    /// Updates the virtual snapshot in-place to reflect any currently-active
    /// steps in this combo for the given timestamp.
    /// </summary>
    public void Apply(ControllerSnapshot physical, ButtonState[] virtualButtons, DateTimeOffset now)
    {
        if (rule.SourceButton == ButtonId.None || rule.Steps.Count == 0)
        {
            return;
        }

        var sourcePressed = physical.IsPressed(rule.SourceButton);

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
            // Nothing to do; ensure source suppression doesn't accidentally re-press.
            if (rule.SuppressSourceButton)
            {
                virtualButtons[(int)rule.SourceButton] = false;
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
                virtualButtons[(int)step.Button] = true;
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
            virtualButtons[(int)rule.SourceButton] = false;
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
