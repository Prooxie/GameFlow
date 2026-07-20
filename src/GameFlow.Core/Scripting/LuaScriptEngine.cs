using GameFlow.Core.Enums;
using GameFlow.Core.Models;
using GameFlow.Core.Models.Rules;
using Microsoft.Extensions.Logging;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace GameFlow.Core.Scripting;

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
///
/// A second, narrower entry point — <see cref="EvaluateCombine"/> — serves
/// <see cref="Models.Rules.MultiSourceCombineRule"/>'s Script mode. It uses
/// a different contract (<c>function evaluate(ctx) return ... end</c>, no
/// ctx.press()/set_left() side-effect API) because that rule kind only
/// ever needs to compute one boolean from a handful of named sources —
/// see that method's doc comment for the exact shape.
/// </summary>
public sealed class LuaScriptEngine : IDisposable
{
    private readonly ILogger<LuaScriptEngine> logger;
    private readonly Dictionary<string, LoadedScript> scripts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LoadedCombineScript> combineScripts = new(StringComparer.Ordinal);
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

    /// <summary>
    /// Evaluates a <see cref="Models.Rules.MultiSourceCombineRule"/> running
    /// in Script mode. Unlike <see cref="Execute"/> — which drives an
    /// entire tick's worth of virtual output imperatively via
    /// ctx.press()/set_left() etc. — this is a narrow "compute one value
    /// from several named inputs" contract:
    /// <code>
    ///   function evaluate(ctx)
    ///       return ctx.A and not ctx.B   -- ctx fields are the row's own
    ///   end                              -- source button names, 0/1 each
    /// </code>
    /// <c>ctx</c> also exposes <c>ctx.now_ms</c>. The return value is
    /// read with normal Lua truthiness (nil/false = not pressed,
    /// everything else = pressed) — there's no ctx.press() or any other
    /// side-effect API here; this script can only ever produce the one
    /// boolean the combine row asked for.
    /// </summary>
    /// <param name="ruleId">Cache key — pass the owning rule's <c>Id</c>.</param>
    /// <param name="scriptCode">The Lua source. Recompiled only when it changes.</param>
    /// <param name="sources">
    /// The combine row's own sources, keyed by <see cref="Enums.ButtonId"/>
    /// name, each 1 (pressed) or 0 (released) this tick.
    /// </param>
    /// <returns>
    /// <see langword="false"/> if disposed, the script is empty, or it
    /// fails to compile/run — same "discard this tick's effect rather
    /// than throw" philosophy as <see cref="Execute"/>.
    /// </returns>
    public bool EvaluateCombine(string ruleId, string scriptCode, IReadOnlyDictionary<string, double> sources, DateTimeOffset now)
    {
        if (disposed || string.IsNullOrWhiteSpace(scriptCode))
        {
            return false;
        }

        LoadedCombineScript loaded;
        lock (gate)
        {
            var hash = scriptCode.GetHashCode();
            if (!combineScripts.TryGetValue(ruleId, out var existing) || existing.SourceHash != hash)
            {
                existing = CompileCombineScript(ruleId, scriptCode, hash);
                combineScripts[ruleId] = existing;
            }
            loaded = existing;
        }

        if (!loaded.IsRunnable)
        {
            return false;
        }

        try
        {
            var ctx = DynValue.NewTable(loaded.Script);
            ctx.Table.Set("now_ms", DynValue.NewNumber((now - DateTimeOffset.UnixEpoch).TotalMilliseconds));
            foreach (var (name, value) in sources)
            {
                ctx.Table.Set(name, DynValue.NewNumber(value));
            }

            return loaded.Script.Call(loaded.Evaluate, ctx).CastToBool();
        }
        catch (ScriptRuntimeException ex)
        {
            logger.LogWarning("Lua runtime error in combine script {RuleId}: {Error}", ruleId, ex.DecoratedMessage);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Lua runtime error in combine script {RuleId}.", ruleId);
            return false;
        }
    }

    private LoadedCombineScript CompileCombineScript(string ruleId, string scriptCode, int hash)
    {
        try
        {
            var script = new Script(CoreModules.Preset_HardSandbox)
            {
                Options = { ScriptLoader = new InvalidScriptLoader() }
            };
            script.DoString(scriptCode);

            var evaluate = script.Globals.Get("evaluate");
            if (evaluate.Type != DataType.Function)
            {
                logger.LogWarning("Combine script {RuleId} does not define an evaluate(ctx) function — disabling.", ruleId);
                return new LoadedCombineScript(null!, DynValue.Nil, hash);
            }

            return new LoadedCombineScript(script, evaluate, hash);
        }
        catch (SyntaxErrorException ex)
        {
            logger.LogWarning("Lua compile error in combine script {RuleId}: {Error}", ruleId, ex.DecoratedMessage);
            return new LoadedCombineScript(null!, DynValue.Nil, hash);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load combine script {RuleId}.", ruleId);
            return new LoadedCombineScript(null!, DynValue.Nil, hash);
        }
    }

    public void Remove(string ruleId)
    {
        lock (gate)
        {
            scripts.Remove(ruleId);
            combineScripts.Remove(ruleId);
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
        combineScripts.Clear();
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

    /// <summary>Compiled state for <see cref="EvaluateCombine"/> — deliberately smaller than <see cref="LoadedScript"/> since combine scripts carry no persistent Lua-side state or ctx callbacks.</summary>
    private sealed record LoadedCombineScript(Script Script, DynValue Evaluate, int SourceHash)
    {
        public bool IsRunnable => Script is not null && Evaluate.Type == DataType.Function;
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
