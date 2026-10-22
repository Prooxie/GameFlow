using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using GameFlow.Core.Scripting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameFlow.Core.Tests;

public sealed class LuaScriptEngineTests
{
    [Theory]
    [InlineData("while true do end")]
    [InlineData("function on_tick(ctx) ctx.press('South'); while true do end end")]
    public void InfiniteLoopsDiscardOutputAndDoNotBlockLaterScripts(string source)
    {
        using var engine = new LuaScriptEngine(NullLogger<LuaScriptEngine>.Instance);
        var rule = new ControlScriptRule { Id = "test", ScriptCode = source };
        Assert.False(Execute(engine, rule)[(int)ButtonId.South]);
        Assert.False(Execute(engine, rule)[(int)ButtonId.South]);
        rule = rule with { ScriptCode = "function on_tick(ctx) ctx.press('South') end" };
        Assert.True(Execute(engine, rule)[(int)ButtonId.South]);
    }

    [Fact]
    public void ReusedCoroutineKeepsStateAndCompletesEachFrame()
    {
        using var engine = new LuaScriptEngine(NullLogger<LuaScriptEngine>.Instance);
        var rule = new ControlScriptRule { Id = "stateful", ScriptCode = "function on_tick(ctx) ctx.state.n = (ctx.state.n or 0) + 1; if ctx.state.n % 2 == 1 then ctx.press('South') end end" };
        for (var i = 0; i < 100; i++) { Assert.Equal(i % 2 == 0, Execute(engine, rule)[(int)ButtonId.South]); }
    }

    [Fact]
    public void AUserYieldCannotCommitPartialOutput()
    {
        using var engine = new LuaScriptEngine(NullLogger<LuaScriptEngine>.Instance);
        var rule = new ControlScriptRule { Id = "yield", ScriptCode = "function on_tick(ctx) ctx.press('South'); coroutine.yield(); ctx.press('North') end" };
        Assert.False(Execute(engine, rule)[(int)ButtonId.South]);
        Assert.False(Execute(engine, rule)[(int)ButtonId.South]);
    }

    [Fact]
    public void SourceLimitCountsUtf8Bytes()
    {
        using var engine = new LuaScriptEngine(NullLogger<LuaScriptEngine>.Instance);
        var rule = new ControlScriptRule { Id = "large", ScriptCode = "--" + new string('é', 40_000) + "\nfunction on_tick(ctx) ctx.press('South') end" };
        Assert.False(Execute(engine, rule)[(int)ButtonId.South]);
    }

    [Fact]
    public void RepeatedTicksDoNotAllocateANewVmStack()
    {
        using var engine = new LuaScriptEngine(NullLogger<LuaScriptEngine>.Instance);
        var rule = new ControlScriptRule { Id = "allocation", ScriptCode = "function on_tick(ctx) ctx.press('South') end" };
        for (var i = 0; i < 10; i++) { Execute(engine, rule); }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) { Execute(engine, rule); }
        // Allows context tables and callbacks; catches the previous ~2 MiB
        // coroutine-stack allocation per tick without relying on wall time.
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread() - before, 0, 100L * 64 * 1024);
    }

    [Fact]
    public void InstructionBudgetRestartsForEachTick()
    {
        using var engine = new LuaScriptEngine(NullLogger<LuaScriptEngine>.Instance);
        var rule = new ControlScriptRule { Id = "budget", ScriptCode = "function on_tick(ctx) local n=0; for i=1,2000 do n=n+i end; ctx.press('South') end" };
        for (var i = 0; i < 20; i++) { Assert.True(Execute(engine, rule)[(int)ButtonId.South]); }
    }

    [Fact]
    public void RuntimeErrorDiscardsOutputAndRetriesAfterTheCooldown()
    {
        using var engine = new LuaScriptEngine(NullLogger<LuaScriptEngine>.Instance);
        var rule = new ControlScriptRule { Id = "retry", ScriptCode = "function on_tick(ctx) ctx.state.n=(ctx.state.n or 0)+1; ctx.press('South'); if ctx.state.n==1 then error('temporary failure') end end" };
        var now = DateTimeOffset.UnixEpoch.AddDays(1);
        Assert.False(Execute(engine, rule, now)[(int)ButtonId.South]);
        Assert.False(Execute(engine, rule, now.AddSeconds(1))[(int)ButtonId.South]);
        Assert.True(Execute(engine, rule, now.AddSeconds(5))[(int)ButtonId.South]);
    }

    [Fact]
    public void NumericButtonOutsideTheArrayDoesNotDiscardValidOutput()
    {
        using var engine = new LuaScriptEngine(NullLogger<LuaScriptEngine>.Instance);
        var rule = new ControlScriptRule { Id = "test", ScriptCode = "function on_tick(ctx) ctx.press('South'); ctx.press('999'); ctx.release('-1') end" };
        Assert.True(Execute(engine, rule)[(int)ButtonId.South]);
    }

    private static bool[] Execute(LuaScriptEngine engine, ControlScriptRule rule, DateTimeOffset? now = null)
    {
        var buttons = new bool[Enum.GetValues<ButtonId>().Length];
        var left = StickVector.Zero;
        var right = StickVector.Zero;
        float lt = 0, rt = 0;
        engine.Execute(rule, new ControllerSnapshot(), new ControllerSnapshot(), buttons, ref left, ref right, ref lt, ref rt, now ?? DateTimeOffset.UtcNow);
        return buttons;
    }
}
