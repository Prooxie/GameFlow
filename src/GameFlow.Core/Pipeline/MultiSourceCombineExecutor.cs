using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using GameFlow.Core.Scripting;

namespace GameFlow.Core.Pipeline;

/// <summary>
/// Per-rule executor for a <see cref="MultiSourceCombineRule"/>.
///
/// <para>
/// Stateless for five of the six modes — they're pure functions of this
/// tick's source states. <see cref="CombineMode.ToggleOnAny"/> is the
/// exception: it needs to remember whether the combined sources were
/// already active last tick (to find the rising edge) and what the
/// toggle's current output is. <see cref="CombineMode.Script"/> keeps no
/// state of its own here — compiled-script caching lives in
/// <see cref="LuaScriptEngine"/>, keyed by this rule's id, exactly like
/// <see cref="ControlScriptRule"/>.
/// </para>
/// </summary>
public sealed class MultiSourceCombineExecutor(MultiSourceCombineRule rule)
{
    private readonly MultiSourceCombineRule rule = rule;
    private bool anySourceWasActiveLastTick;
    private bool toggledOn;

    public void Apply(Dictionary<ButtonId, bool> virtualButtons, LuaScriptEngine? scriptEngine, DateTimeOffset now)
    {
        if (rule.TargetButton == ButtonId.None || rule.Sources.Count == 0)
        {
            return;
        }

        var states = new bool[rule.Sources.Count];
        for (var i = 0; i < rule.Sources.Count; i++)
        {
            states[i] = virtualButtons.TryGetValue(rule.Sources[i], out var pressed) && pressed;
        }

        virtualButtons[rule.TargetButton] = Evaluate(states, scriptEngine, now);

        if (rule.SuppressSources)
        {
            foreach (var source in rule.Sources)
            {
                if (source != rule.TargetButton)
                {
                    virtualButtons[source] = false;
                }
            }
        }
    }

    private bool Evaluate(bool[] states, LuaScriptEngine? scriptEngine, DateTimeOffset now)
    {
        var activeCount = 0;
        foreach (var s in states)
        {
            if (s) { activeCount++; }
        }

        return rule.Strategy switch
        {
            CombineMode.Any => activeCount > 0,
            CombineMode.All => activeCount == states.Length,
            CombineMode.None => activeCount == 0,
            CombineMode.ExactlyOne => activeCount == 1,
            CombineMode.Majority => activeCount * 2 > states.Length,
            CombineMode.ToggleOnAny => EvaluateToggle(activeCount > 0),
            CombineMode.Script => EvaluateScript(states, scriptEngine, now),
            _ => false
        };
    }

    private bool EvaluateToggle(bool anySourceActiveNow)
    {
        if (anySourceActiveNow && !anySourceWasActiveLastTick)
        {
            toggledOn = !toggledOn;
        }
        anySourceWasActiveLastTick = anySourceActiveNow;
        return toggledOn;
    }

    private bool EvaluateScript(bool[] states, LuaScriptEngine? scriptEngine, DateTimeOffset now)
    {
        if (scriptEngine is null || string.IsNullOrWhiteSpace(rule.ScriptCode))
        {
            return false;
        }

        // Duplicate ButtonId entries in Sources collapse to one ctx field —
        // a degenerate authoring case we don't need to guard against harder
        // than "last one wins" here.
        var named = new Dictionary<string, double>(rule.Sources.Count, StringComparer.Ordinal);
        for (var i = 0; i < rule.Sources.Count; i++)
        {
            named[rule.Sources[i].ToString()] = states[i] ? 1d : 0d;
        }

        return scriptEngine.EvaluateCombine(rule.Id, rule.ScriptCode, named, now);
    }

    /// <summary>Resets toggle state. Called when rules are swapped out.</summary>
    public void Reset()
    {
        anySourceWasActiveLastTick = false;
        toggledOn = false;
    }
}
