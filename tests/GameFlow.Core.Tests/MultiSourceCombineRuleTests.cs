using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using GameFlow.Core.Pipeline;
using GameFlow.Core.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Core.Tests;

public sealed class MultiSourceCombineRuleTests
{
    private static ControllerSnapshot SnapshotWith(params ButtonId[] pressed)
    {
        var buttons = ButtonState.Clone(ButtonState.CreateEmptyMap());
        foreach (var button in pressed)
        {
            buttons[button] = true;
        }
        return new ControllerSnapshot { Buttons = buttons };
    }

    private static ControllerFrameResult Run(MultiSourceCombineRule rule, params ButtonId[] pressed)
    {
        var profile = new ProfileDocument { Rules = [rule] };
        var pipeline = new ControllerMappingPipeline(profile);
        return pipeline.Process(SnapshotWith(pressed), DateTimeOffset.UtcNow);
    }

    [Fact]
    public void Any_IsPressedWhenAtLeastOneSourceIsPressed()
    {
        var rule = new MultiSourceCombineRule
        {
            Id = "combine",
            Name = "Any",
            Sources = [ButtonId.South, ButtonId.East],
            Strategy = CombineMode.Any,
            TargetButton = ButtonId.North
        };

        Assert.True(Run(rule, ButtonId.South).VirtualSnapshot.IsPressed(ButtonId.North));
        Assert.False(Run(rule).VirtualSnapshot.IsPressed(ButtonId.North));
    }

    [Fact]
    public void All_RequiresEverySourcePressed()
    {
        var rule = new MultiSourceCombineRule
        {
            Id = "combine",
            Name = "All",
            Sources = [ButtonId.South, ButtonId.East],
            Strategy = CombineMode.All,
            TargetButton = ButtonId.North
        };

        Assert.False(Run(rule, ButtonId.South).VirtualSnapshot.IsPressed(ButtonId.North));
        Assert.True(Run(rule, ButtonId.South, ButtonId.East).VirtualSnapshot.IsPressed(ButtonId.North));
    }

    [Fact]
    public void None_IsPressedOnlyWhenNoSourceIsPressed()
    {
        var rule = new MultiSourceCombineRule
        {
            Id = "combine",
            Name = "None",
            Sources = [ButtonId.South, ButtonId.East],
            Strategy = CombineMode.None,
            TargetButton = ButtonId.North
        };

        Assert.True(Run(rule).VirtualSnapshot.IsPressed(ButtonId.North));
        Assert.False(Run(rule, ButtonId.South).VirtualSnapshot.IsPressed(ButtonId.North));
    }

    [Fact]
    public void ExactlyOne_RequiresPreciselyOneSource()
    {
        var rule = new MultiSourceCombineRule
        {
            Id = "combine",
            Name = "XOR",
            Sources = [ButtonId.South, ButtonId.East, ButtonId.West],
            Strategy = CombineMode.ExactlyOne,
            TargetButton = ButtonId.North
        };

        Assert.False(Run(rule).VirtualSnapshot.IsPressed(ButtonId.North));
        Assert.True(Run(rule, ButtonId.South).VirtualSnapshot.IsPressed(ButtonId.North));
        Assert.False(Run(rule, ButtonId.South, ButtonId.East).VirtualSnapshot.IsPressed(ButtonId.North));
    }

    [Fact]
    public void Majority_RequiresMoreThanHalf()
    {
        var rule = new MultiSourceCombineRule
        {
            Id = "combine",
            Name = "Majority",
            Sources = [ButtonId.South, ButtonId.East, ButtonId.West],
            Strategy = CombineMode.Majority,
            TargetButton = ButtonId.North
        };

        Assert.False(Run(rule, ButtonId.South).VirtualSnapshot.IsPressed(ButtonId.North));
        Assert.True(Run(rule, ButtonId.South, ButtonId.East).VirtualSnapshot.IsPressed(ButtonId.North));
    }

    [Fact]
    public void ToggleOnAny_FlipsOnEachRisingEdgeOfTheCombinedSources()
    {
        var rule = new MultiSourceCombineRule
        {
            Id = "combine",
            Name = "Toggle",
            Sources = [ButtonId.South, ButtonId.East],
            Strategy = CombineMode.ToggleOnAny,
            TargetButton = ButtonId.North
        };
        var profile = new ProfileDocument { Rules = [rule] };
        var pipeline = new ControllerMappingPipeline(profile);
        var now = DateTimeOffset.UtcNow;

        // Press South: rising edge of "any source" -> toggles on.
        Assert.True(pipeline.Process(SnapshotWith(ButtonId.South), now).VirtualSnapshot.IsPressed(ButtonId.North));

        // Still held: no new rising edge, stays on.
        Assert.True(pipeline.Process(SnapshotWith(ButtonId.South), now.AddMilliseconds(16)).VirtualSnapshot.IsPressed(ButtonId.North));

        // Released: no rising edge either, toggle persists.
        Assert.True(pipeline.Process(SnapshotWith(), now.AddMilliseconds(32)).VirtualSnapshot.IsPressed(ButtonId.North));

        // A DIFFERENT source rising still counts as "any source" rising -> toggles off.
        Assert.False(pipeline.Process(SnapshotWith(ButtonId.East), now.AddMilliseconds(48)).VirtualSnapshot.IsPressed(ButtonId.North));
    }

    [Fact]
    public void SuppressSources_ClearsListedSourcesButNotTheTarget()
    {
        var rule = new MultiSourceCombineRule
        {
            Id = "combine",
            Name = "Suppress",
            Sources = [ButtonId.South, ButtonId.East],
            Strategy = CombineMode.Any,
            TargetButton = ButtonId.North,
            SuppressSources = true
        };

        var result = Run(rule, ButtonId.South);

        Assert.True(result.VirtualSnapshot.IsPressed(ButtonId.North));
        Assert.False(result.VirtualSnapshot.IsPressed(ButtonId.South));
    }

    [Fact]
    public void Passthrough_LeavesTargetUntouchedEvenWhenCombineLogicWouldHaveClearedIt()
    {
        // West -> North via an upstream remap. South (the combine rule's
        // only source) is NOT pressed, so if the Passthrough rule actually
        // ran its Any logic, it would force North back to false. It must not.
        var remap = new ButtonRemapRule
        {
            Id = "remap",
            Name = "Remap",
            SourceButton = ButtonId.West,
            TargetButton = ButtonId.North
        };
        var combine = new MultiSourceCombineRule
        {
            Id = "combine",
            Name = "Passthrough",
            Sources = [ButtonId.South],
            Strategy = CombineMode.Any,
            TargetButton = ButtonId.North,
            Mode = RuleMode.Passthrough
        };
        var profile = new ProfileDocument { Rules = [remap, combine] };
        var pipeline = new ControllerMappingPipeline(profile);

        var result = pipeline.Process(SnapshotWith(ButtonId.West), DateTimeOffset.UtcNow);

        Assert.True(result.VirtualSnapshot.IsPressed(ButtonId.North));
    }

    [Fact]
    public void DoNothing_ClearsTheTargetButtonEvenIfAnUpstreamRuleSetIt()
    {
        var remap = new ButtonRemapRule
        {
            Id = "remap",
            Name = "Remap",
            SourceButton = ButtonId.South,
            TargetButton = ButtonId.North
        };
        var combine = new MultiSourceCombineRule
        {
            Id = "combine",
            Name = "DoNothing",
            Sources = [ButtonId.South],
            Strategy = CombineMode.Any,
            TargetButton = ButtonId.North,
            Mode = RuleMode.DoNothing
        };
        var profile = new ProfileDocument { Rules = [remap, combine] };
        var pipeline = new ControllerMappingPipeline(profile);

        var result = pipeline.Process(SnapshotWith(ButtonId.South), DateTimeOffset.UtcNow);

        Assert.False(result.VirtualSnapshot.IsPressed(ButtonId.North));
    }

    [Fact]
    public void ChainsWithAnUpstreamRemapRule()
    {
        // South -> East via remap, then a combine row (East OR West) -> North.
        var remap = new ButtonRemapRule
        {
            Id = "remap",
            Name = "Remap",
            SourceButton = ButtonId.South,
            TargetButton = ButtonId.East
        };
        var combine = new MultiSourceCombineRule
        {
            Id = "combine",
            Name = "Chain",
            Sources = [ButtonId.East, ButtonId.West],
            Strategy = CombineMode.Any,
            TargetButton = ButtonId.North
        };
        var profile = new ProfileDocument { Rules = [remap, combine] };
        var pipeline = new ControllerMappingPipeline(profile);

        var result = pipeline.Process(SnapshotWith(ButtonId.South), DateTimeOffset.UtcNow);

        Assert.True(result.VirtualSnapshot.IsPressed(ButtonId.North));
    }

    [Fact]
    public void ScriptStrategy_WithNoEngineAttached_LeavesTargetFalseAndAddsANote()
    {
        var rule = new MultiSourceCombineRule
        {
            Id = "combine",
            Name = "Script",
            Sources = [ButtonId.South],
            Strategy = CombineMode.Script,
            TargetButton = ButtonId.North,
            ScriptCode = "function evaluate(ctx) return ctx.South > 0 end"
        };

        var result = Run(rule, ButtonId.South);

        Assert.False(result.VirtualSnapshot.IsPressed(ButtonId.North));
        Assert.Contains(result.Notes, n => n.Contains("Script strategy"));
    }

    [Fact]
    public void ScriptStrategy_WithEngineAttached_DoesNotAddTheNoEngineNote()
    {
        // This only proves the wiring dispatches to the engine instead of
        // short-circuiting — it can't prove the Lua evaluates to a specific
        // value without a real MoonSharp package, which this sandbox can't
        // restore. Run `dotnet test` on your machine to confirm the actual
        // evaluated result.
        var rule = new MultiSourceCombineRule
        {
            Id = "combine",
            Name = "Script",
            Sources = [ButtonId.South],
            Strategy = CombineMode.Script,
            TargetButton = ButtonId.North,
            ScriptCode = "function evaluate(ctx) return ctx.South > 0 end"
        };
        var profile = new ProfileDocument { Rules = [rule] };
        var engine = new LuaScriptEngine(NullLogger<LuaScriptEngine>.Instance);
        var pipeline = new ControllerMappingPipeline(profile, engine);

        var result = pipeline.Process(SnapshotWith(ButtonId.South), DateTimeOffset.UtcNow);

        Assert.False(result.Notes.Any(n => n.Contains("no script engine is attached")));
    }
}
