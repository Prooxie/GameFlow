using Autofire.Core.Enums;
using Autofire.Core.Models;
using Autofire.Core.Models.Rules;
using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace Autofire.Core.Scripting;

/// <summary>
/// Lua scripting engine for <see cref="ControlScriptRule"/> instances.
///
/// Why Lua and not C#?
///   • C# scripting (Roslyn / CSharpScript) has a multi-second JIT cost on cold start.
///   • Lua scripts compile in under 50 ms and execute in ~microseconds per tick — a must
///     for a 1000 Hz polling loop.
///   • MoonSharp is a pure-managed Lua 5.x interpreter (no native deps), so the engine
///     ships in the same x-platform NuGet bundle as the rest of the app.
///
/// Sandbox:
///   • The default script sandbox excludes <c>os</c>, <c>io</c>, <c>debug</c>,
///     <c>require</c>, <c>load</c>, <c>loadfile</c>, <c>dofile</c>, and <c>package</c>.
///   • The script sees a single global, <c>ctx</c>, with per-tick fields:
///       ctx.left.x, ctx.left.y          -- left stick (-1..1)
///       ctx.right.x, ctx.right.y        -- right stick
///       ctx.lt, ctx.rt                  -- triggers (0..1)
///       ctx.is_pressed("South") -> bool -- physical button query
///       ctx.press("South")              -- emit virtual button this tick
///       ctx.release("South")            -- explicitly clear a virtual button
///       ctx.set_left(x, y)              -- write virtual left stick
///       ctx.set_right(x, y)
///       ctx.set_lt(value), ctx.set_rt(value)
///       ctx.now_ms                      -- script-local monotonic clock in ms
///       ctx.dt_ms                       -- ms since last invocation of this script
///       ctx.state                       -- a per-script Lua table that persists across ticks
///
/// Failure handling:
///   • Compile errors are logged once and the script is disabled until edited.
///   • Runtime errors are throttled (1 message every 5 s) and the script's effect
///     on this tick is discarded.
/// </summary>
public sealed class LuaScriptEngine : IDisposable
{
    private readonly ILogger<LuaScriptEngine> logger;
    private readonly Dictionary<string, LoadedScript> scripts = new(StringComparer.Ordinal);
    private readonly Lock gate = new();
    private bool disposed;

    static LuaScriptEngine()
    {
        // Tell MoonSharp not to try to load any modules from disk — pure in-memory only.
        Script.GlobalOptions.Platform = new MoonSharp.Interpreter.Platforms.LimitedPlatformAccessor();
        UserData.RegisterAssembly(typeof(LuaScriptEngine).Assembly);
    }

    public LuaScriptEngine(ILogger<LuaScriptEngine> logger)
    {
        this.logger = logger;
    }

    public void EnsureCompiled(ControlScriptRule rule)
    {
        if (disposed)
        {
            return;
        }

        ArgumentNullException.ThrowIfNull(rule);

        lock (gate)
        {
            if (scripts.TryGetValue(rule.Id, out var existing) && existing.SourceHash == rule.ScriptCode.GetHashCode())
            {
                return;
            }

            try
            {
                var script = new Script(CoreModules.Preset_HardSandbox)
                {
                    Options =
                    {
                        ScriptLoader = new InvalidScriptLoader()
                    }
                };

                script.DoString(rule.ScriptCode ?? string.Empty);

                var onTick = script.Globals.Get("on_tick");
                if (onTick.Type != DataType.Function)
                {
                    logger.LogWarning("Lua script {RuleId} ({Name}) does not define an on_tick(ctx) function — disabling.",
                        rule.Id, rule.Name);
                    scripts[rule.Id] = LoadedScript.Disabled(rule.ScriptCode?.GetHashCode() ?? 0);
                    return;
                }

                scripts[rule.Id] = new LoadedScript(
                    Script: script,
                    OnTick: onTick,
                    SourceHash: rule.ScriptCode?.GetHashCode() ?? 0,
                    State: DynValue.NewTable(script),
                    LastError: null,
                    LastErrorAtUtc: null,
                    LastInvokeAtUtc: DateTimeOffset.MinValue);
            }
            catch (SyntaxErrorException ex)
            {
                logger.LogWarning("Lua compile error in script {RuleId} ({Name}): {Error}",
                    rule.Id, rule.Name, ex.DecoratedMessage);
                scripts[rule.Id] = LoadedScript.Disabled(rule.ScriptCode?.GetHashCode() ?? 0);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load Lua script {RuleId} ({Name}).", rule.Id, rule.Name);
                scripts[rule.Id] = LoadedScript.Disabled(rule.ScriptCode?.GetHashCode() ?? 0);
            }
        }
    }

    public void Execute(ControlScriptRule rule, ControllerSnapshot physical, ControllerSnapshot virtualBefore,
                        bool[] virtualButtons, ref StickVector virtualLeft, ref StickVector virtualRight,
                        ref float virtualLt, ref float virtualRt, DateTimeOffset now)
    {
        if (disposed)
        {
            return;
        }

        EnsureCompiled(rule);

        LoadedScript loaded;
        lock (gate)
        {
            if (!scripts.TryGetValue(rule.Id, out var current) || !current.IsRunnable)
            {
                return;
            }
            loaded = current;
        }

        try
        {
            var dtMs = loaded.LastInvokeAtUtc == DateTimeOffset.MinValue
                ? 0
                : (now - loaded.LastInvokeAtUtc).TotalMilliseconds;

            // Build ctx table fresh each tick — cheap because Lua tables are pooled internally.
            var ctx = DynValue.NewTable(loaded.Script);
            var t   = ctx.Table;
            t.Set("now_ms", DynValue.NewNumber((now - DateTimeOffset.UnixEpoch).TotalMilliseconds));
            t.Set("dt_ms",  DynValue.NewNumber(dtMs));
            t.Set("state",  loaded.State);

            t.Set("left",  Stick(loaded.Script, physical.LeftStick));
            t.Set("right", Stick(loaded.Script, physical.RightStick));
            t.Set("lt",    DynValue.NewNumber(physical.LeftTrigger));
            t.Set("rt",    DynValue.NewNumber(physical.RightTrigger));

            // Local copies that get written back after the script returns.
            var localButtons = (bool[])virtualButtons.Clone();
            var localLeft    = virtualLeft;
            var localRight   = virtualRight;
            var localLt      = virtualLt;
            var localRt      = virtualRt;

            t.Set("is_pressed", DynValue.NewCallback((_, args) =>
            {
                if (args.Count <= 0) return DynValue.False;
                var name = args[0].CastToString();
                return DynValue.NewBoolean(
                    Enum.TryParse<ButtonId>(name, true, out var b) && physical.IsPressed(b));
            }));

            t.Set("press", DynValue.NewCallback((_, args) =>
            {
                if (args.Count <= 0) return DynValue.Nil;
                if (Enum.TryParse<ButtonId>(args[0].CastToString(), true, out var b))
                {
                    localButtons[(int)b] = true;
                }
                return DynValue.Nil;
            }));

            t.Set("release", DynValue.NewCallback((_, args) =>
            {
                if (args.Count <= 0) return DynValue.Nil;
                if (Enum.TryParse<ButtonId>(args[0].CastToString(), true, out var b))
                {
                    localButtons[(int)b] = false;
                }
                return DynValue.Nil;
            }));

            t.Set("set_left", DynValue.NewCallback((_, args) =>
            {
                localLeft = ReadStick(args, localLeft);
                return DynValue.Nil;
            }));

            t.Set("set_right", DynValue.NewCallback((_, args) =>
            {
                localRight = ReadStick(args, localRight);
                return DynValue.Nil;
            }));

            t.Set("set_lt", DynValue.NewCallback((_, args) =>
            {
                if (args.Count >= 1) localLt = (float)Math.Clamp(args[0].CastToNumber() ?? 0d, 0d, 1d);
                return DynValue.Nil;
            }));

            t.Set("set_rt", DynValue.NewCallback((_, args) =>
            {
                if (args.Count >= 1) localRt = (float)Math.Clamp(args[0].CastToNumber() ?? 0d, 0d, 1d);
                return DynValue.Nil;
            }));

            _ = loaded.Script.Call(loaded.OnTick, ctx);

            // Commit.
            for (var i = 0; i < virtualButtons.Length; i++)
            {
                if (localButtons[i] != virtualButtons[i])
                {
                    virtualButtons[i] = localButtons[i];
                }
            }
            virtualLeft  = localLeft;
            virtualRight = localRight;
            virtualLt    = localLt;
            virtualRt    = localRt;

            lock (gate)
            {
                if (scripts.TryGetValue(rule.Id, out var s))
                {
                    scripts[rule.Id] = s with { LastInvokeAtUtc = now };
                }
            }
        }
        catch (ScriptRuntimeException ex)
        {
            ReportRuntimeError(rule, loaded, ex.DecoratedMessage, now);
        }
        catch (Exception ex)
        {
            ReportRuntimeError(rule, loaded, ex.Message, now);
        }
    }

    public void Remove(string ruleId)
    {
        lock (gate)
        {
            scripts.Remove(ruleId);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        scripts.Clear();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static DynValue Stick(Script s, StickVector v)
    {
        var t = DynValue.NewTable(s);
        t.Table.Set("x", DynValue.NewNumber(v.X));
        t.Table.Set("y", DynValue.NewNumber(v.Y));
        return t;
    }

    private static StickVector ReadStick(CallbackArguments args, StickVector fallback)
    {
        if (args.Count >= 2)
        {
            var x = (float)Math.Clamp(args[0].CastToNumber() ?? 0d, -1d, 1d);
            var y = (float)Math.Clamp(args[1].CastToNumber() ?? 0d, -1d, 1d);
            return new StickVector(x, y);
        }
        return fallback;
    }

    private void ReportRuntimeError(ControlScriptRule rule, LoadedScript loaded, string message, DateTimeOffset now)
    {
        const int throttleSeconds = 5;

        lock (gate)
        {
            var lastError = loaded.LastErrorAtUtc;
            if (lastError is not null && (now - lastError.Value).TotalSeconds < throttleSeconds)
            {
                return;
            }

            scripts[rule.Id] = loaded with
            {
                LastError = message,
                LastErrorAtUtc = now
            };
        }

        logger.LogWarning("Lua runtime error in script {RuleId} ({Name}): {Error}",
            rule.Id, rule.Name, message);
    }

    private sealed record LoadedScript(
        Script Script,
        DynValue OnTick,
        int SourceHash,
        DynValue State,
        string? LastError,
        DateTimeOffset? LastErrorAtUtc,
        DateTimeOffset LastInvokeAtUtc)
    {
        public bool IsRunnable => Script is not null && OnTick.Type == DataType.Function;

        public static LoadedScript Disabled(int sourceHash) => new(
            Script: null!,
            OnTick: DynValue.Nil,
            SourceHash: sourceHash,
            State: DynValue.Nil,
            LastError: "compile failed",
            LastErrorAtUtc: DateTimeOffset.UtcNow,
            LastInvokeAtUtc: DateTimeOffset.MinValue);
    }

    /// <summary>
    /// Refuses every <c>require</c>/<c>dofile</c>/<c>loadfile</c> attempt — the
    /// engine never reads or writes the disk on behalf of a user script.
    /// </summary>
    private sealed class InvalidScriptLoader : IScriptLoader
    {
        public bool ScriptFileExists(string name) => false;
        public object LoadFile(string file, Table globalContext) =>
            throw new ScriptRuntimeException("dofile/require/loadfile are disabled in the Autofire Lua sandbox.");
        public string ResolveFileName(string filename, Table globalContext) => filename;
        public string ResolveModuleName(string modname, Table globalContext) => modname;
    }
}
