using System.Globalization;

namespace Autofire.Infrastructure.Theming.Flee;

/// <summary>
/// AST node types for the trimmed Flee dialect the theme engine accepts.
///
/// <para>
/// VSCView themes embed Flee formulas as plain strings inside the JSON
/// (<c>"input"</c>, <c>"inputX"</c>, <c>"min"</c>, <c>"max"</c>, etc.).
/// At theme-load time each formula is tokenized once into the AST below
/// and the resulting node is cached on the owning theme element so that
/// the 60 Hz render path only walks an already-built tree.
/// </para>
///
/// <para>
/// Supported surface area (matches the examples in VSCView's
/// THEMEENGINE.md):
/// </para>
/// <list type="bullet">
/// <item>numeric literals (<c>0.25</c>, <c>15</c>, <c>1.0</c>);</item>
/// <item>boolean literals (<c>true</c>, <c>false</c>);</item>
/// <item>variable references such as <c>stick_left:x</c>,
///   <c>quad_right:s</c>, <c>triggers:l:analog</c>;</item>
/// <item>unary <c>-</c> and <c>not</c>;</item>
/// <item>arithmetic <c>+ - * /</c>;</item>
/// <item>relational <c>&lt; &lt;= &gt; &gt;= = == != &lt;&gt;</c>;</item>
/// <item>logical <c>and</c>, <c>or</c>;</item>
/// <item>parentheses for grouping;</item>
/// <item>function calls: <c>max</c>, <c>min</c>, <c>if</c>, <c>tobool</c>
///   and any <c>math.&lt;name&gt;</c> dispatched against
///   <see cref="System.Math"/>.</item>
/// </list>
///
/// <para>
/// What's intentionally <b>not</b> supported: assignments, indexers,
/// string literals, member access other than the <c>math.</c> namespace,
/// and the legacy Flee escape-percent operator. Any of those parse to
/// <see cref="FleeParseException"/> so we fail loudly at theme load rather
/// than silently miscompute at render time.
/// </para>
/// </summary>
public abstract record FleeNode
{
    /// <summary>
    /// Evaluates the node against the supplied symbol table. Returns a
    /// <see cref="double"/> because every controller-state variable in
    /// the VSCView dialect is numeric (0/1 for buttons, -1..1 for sticks).
    /// Boolean operators coerce 0 → false / non-zero → true on the way in
    /// and re-encode results as 0/1 on the way out so consumers never
    /// have to keep two numeric types in sync.
    /// </summary>
    public abstract double Evaluate(IFleeSymbols symbols);
}

/// <summary>Numeric or boolean literal.</summary>
public sealed record LiteralNode(double Value) : FleeNode
{
    public override double Evaluate(IFleeSymbols symbols) => Value;
}

/// <summary>
/// Variable reference. The name preserves the colon-separated form from
/// the JSON (<c>"quad_right:s"</c>, <c>"triggers:l:analog"</c>) so the
/// resolver in <see cref="IFleeSymbols"/> can look up its host control
/// and then the specific sub-axis without re-parsing.
///
/// <para>
/// A handful of <c>math.</c>-prefixed names — <c>math.pi</c> and
/// <c>math.e</c> — are common-enough constants that authors write them
/// without parentheses, e.g. <c>"input": "stick_left:x * math.pi"</c>.
/// VSCView accepts both spellings; we recognise them here so they
/// don't accidentally bind to the user's symbol table and return 0.
/// </para>
/// </summary>
public sealed record VariableNode(string Name) : FleeNode
{
    public override double Evaluate(IFleeSymbols symbols)
    {
        // Constant-style math.* lookups first — these never reach the
        // symbol resolver, so a theme can read them on a controller
        // that's still initialising (snapshot empty, all axes zero).
        if (Name.Length > 5 && Name.StartsWith("math.", StringComparison.OrdinalIgnoreCase))
        {
            var tail = Name.AsSpan(5);
            if (tail.Equals("pi", StringComparison.OrdinalIgnoreCase)) { return Math.PI; }
            if (tail.Equals("e",  StringComparison.OrdinalIgnoreCase)) { return Math.E; }
        }
        return symbols.Resolve(Name);
    }
}

/// <summary>Unary <c>-x</c> or <c>not x</c>.</summary>
public sealed record UnaryNode(string Op, FleeNode Operand) : FleeNode
{
    public override double Evaluate(IFleeSymbols symbols)
    {
        var v = Operand.Evaluate(symbols);
        return Op switch
        {
            "-"   => -v,
            "not" => v == 0 ? 1 : 0,
            _     => throw new InvalidOperationException($"Unknown unary operator '{Op}'.")
        };
    }
}

/// <summary>Binary arithmetic, comparison or logical operator.</summary>
public sealed record BinaryNode(string Op, FleeNode Left, FleeNode Right) : FleeNode
{
    public override double Evaluate(IFleeSymbols symbols)
    {
        // Short-circuit evaluation for AND / OR keeps theme formulas
        // cheap even when the left-hand side is enough to decide the
        // result (e.g. `tobool(home) and motion:gyro_active`).
        if (Op == "and")
        {
            return Left.Evaluate(symbols) != 0 && Right.Evaluate(symbols) != 0 ? 1 : 0;
        }
        if (Op == "or")
        {
            return Left.Evaluate(symbols) != 0 || Right.Evaluate(symbols) != 0 ? 1 : 0;
        }

        var l = Left.Evaluate(symbols);
        var r = Right.Evaluate(symbols);
        return Op switch
        {
            "+"  => l + r,
            "-"  => l - r,
            "*"  => l * r,
            "/"  => r == 0 ? 0 : l / r,
            "<"  => l <  r ? 1 : 0,
            "<=" => l <= r ? 1 : 0,
            ">"  => l >  r ? 1 : 0,
            ">=" => l >= r ? 1 : 0,
            "==" => l == r ? 1 : 0,
            "="  => l == r ? 1 : 0,
            "!=" => l != r ? 1 : 0,
            "<>" => l != r ? 1 : 0,
            _    => throw new InvalidOperationException($"Unknown binary operator '{Op}'.")
        };
    }
}

/// <summary>
/// Function call. <paramref name="Name"/> is lower-cased on parse so the
/// dispatch table stays case-insensitive without per-call allocations.
/// </summary>
public sealed record CallNode(string Name, IReadOnlyList<FleeNode> Arguments) : FleeNode
{
    public override double Evaluate(IFleeSymbols symbols)
    {
        // Inline the small fixed dispatch table to avoid a dictionary
        // lookup on every render tick. The switch is purely on a short
        // lower-case string so the JIT compiles it to a hash-based jump.
        switch (Name)
        {
            case "max":
            {
                if (Arguments.Count == 0) { return 0; }
                var result = Arguments[0].Evaluate(symbols);
                for (var i = 1; i < Arguments.Count; i++)
                {
                    var v = Arguments[i].Evaluate(symbols);
                    if (v > result) { result = v; }
                }
                return result;
            }

            case "min":
            {
                if (Arguments.Count == 0) { return 0; }
                var result = Arguments[0].Evaluate(symbols);
                for (var i = 1; i < Arguments.Count; i++)
                {
                    var v = Arguments[i].Evaluate(symbols);
                    if (v < result) { result = v; }
                }
                return result;
            }

            case "if":
            {
                if (Arguments.Count != 3)
                {
                    throw new InvalidOperationException(
                        "Flee 'if' requires exactly 3 arguments (condition, ifTrue, ifFalse).");
                }
                return Arguments[0].Evaluate(symbols) != 0
                    ? Arguments[1].Evaluate(symbols)
                    : Arguments[2].Evaluate(symbols);
            }

            case "tobool":
            {
                if (Arguments.Count != 1)
                {
                    throw new InvalidOperationException("Flee 'tobool' requires exactly 1 argument.");
                }
                return Arguments[0].Evaluate(symbols) != 0 ? 1 : 0;
            }

            case "abs":   return Math.Abs(Arguments[0].Evaluate(symbols));
            case "sign":  return Math.Sign(Arguments[0].Evaluate(symbols));
            case "floor": return Math.Floor(Arguments[0].Evaluate(symbols));
            case "ceil":
            case "ceiling": return Math.Ceiling(Arguments[0].Evaluate(symbols));
            case "round": return Math.Round(Arguments[0].Evaluate(symbols));
            case "sqrt":  return Math.Sqrt(Arguments[0].Evaluate(symbols));
            case "clamp":
            {
                if (Arguments.Count != 3)
                {
                    throw new InvalidOperationException("Flee 'clamp' requires (value, min, max).");
                }
                return Math.Clamp(
                    Arguments[0].Evaluate(symbols),
                    Arguments[1].Evaluate(symbols),
                    Arguments[2].Evaluate(symbols));
            }
        }

        // math.<name> namespace — dispatches to System.Math by reflection
        // at *parse* time would be cleaner; for now we route at eval time
        // because the surface area is small enough that the perf cost is
        // negligible against the rest of the render pipeline.
        if (Name.StartsWith("math.", StringComparison.Ordinal))
        {
            return EvaluateMathCall(Name.AsSpan(5), symbols);
        }

        throw new InvalidOperationException($"Unknown Flee function '{Name}'.");
    }

    private double EvaluateMathCall(ReadOnlySpan<char> name, IFleeSymbols symbols)
    {
        // Materialise arguments once so a unary span like `math.sin`
        // doesn't allocate on every tick. The list is typically size 1
        // for math.* so this is a 1-element stack array in practice.
        Span<double> args = stackalloc double[Math.Max(1, Arguments.Count)];
        for (var i = 0; i < Arguments.Count; i++)
        {
            args[i] = Arguments[i].Evaluate(symbols);
        }

        // Case-insensitive comparison so theme.json authors can write
        // either `math.Max` (matching the .NET docs) or `math.max`.
        if (name.Equals("abs",     StringComparison.OrdinalIgnoreCase)) { return Math.Abs(args[0]); }
        if (name.Equals("sign",    StringComparison.OrdinalIgnoreCase)) { return Math.Sign(args[0]); }
        if (name.Equals("floor",   StringComparison.OrdinalIgnoreCase)) { return Math.Floor(args[0]); }
        if (name.Equals("ceiling", StringComparison.OrdinalIgnoreCase)) { return Math.Ceiling(args[0]); }
        if (name.Equals("round",   StringComparison.OrdinalIgnoreCase)) { return Math.Round(args[0]); }
        if (name.Equals("sqrt",    StringComparison.OrdinalIgnoreCase)) { return Math.Sqrt(args[0]); }
        if (name.Equals("sin",     StringComparison.OrdinalIgnoreCase)) { return Math.Sin(args[0]); }
        if (name.Equals("cos",     StringComparison.OrdinalIgnoreCase)) { return Math.Cos(args[0]); }
        if (name.Equals("tan",     StringComparison.OrdinalIgnoreCase)) { return Math.Tan(args[0]); }
        if (name.Equals("atan",    StringComparison.OrdinalIgnoreCase)) { return Math.Atan(args[0]); }
        if (name.Equals("atan2",   StringComparison.OrdinalIgnoreCase)) { return Math.Atan2(args[0], args[1]); }
        if (name.Equals("log",     StringComparison.OrdinalIgnoreCase)) { return Math.Log(args[0]); }
        if (name.Equals("log10",   StringComparison.OrdinalIgnoreCase)) { return Math.Log10(args[0]); }
        if (name.Equals("exp",     StringComparison.OrdinalIgnoreCase)) { return Math.Exp(args[0]); }
        if (name.Equals("pow",     StringComparison.OrdinalIgnoreCase)) { return Math.Pow(args[0], args[1]); }
        if (name.Equals("max",     StringComparison.OrdinalIgnoreCase)) { return Math.Max(args[0], args[1]); }
        if (name.Equals("min",     StringComparison.OrdinalIgnoreCase)) { return Math.Min(args[0], args[1]); }
        if (name.Equals("pi",      StringComparison.OrdinalIgnoreCase)) { return Math.PI; }
        if (name.Equals("e",       StringComparison.OrdinalIgnoreCase)) { return Math.E; }

        throw new InvalidOperationException(
            $"Unsupported math function 'math.{name}'. " +
            $"Add it to FleeAst.EvaluateMathCall if your theme requires it.");
    }
}

/// <summary>
/// Symbol-resolution callback handed to <see cref="FleeNode.Evaluate"/>.
/// Implementations look up colon-qualified VSCView variable names like
/// <c>quad_right:s</c> or <c>triggers:l:analog</c> and return the matching
/// numeric value (0/1 for buttons, -1..1 for sticks, 0..1 for triggers).
/// Unknown names return 0 by convention so a theme written for a richer
/// controller still renders (with missing buttons treated as un-pressed).
/// </summary>
public interface IFleeSymbols
{
    /// <summary>Resolve a colon-qualified variable name to a numeric value.</summary>
    double Resolve(string name);
}

/// <summary>Thrown by <see cref="FleeParser"/> when a formula cannot be parsed.</summary>
public sealed class FleeParseException(string message) : Exception(message)
{
    /// <summary>Convenience factory keeping site-of-error info in the message.</summary>
    public static FleeParseException At(int position, string expected, string source) =>
        new($"Flee parse error at position {position}: expected {expected}. Source: \"{source}\".");
}

// ─── Number-parsing helper shared by tokenizer and tests ────────────────────

/// <summary>
/// Invariant-culture numeric parser. Centralised so the tokenizer and
/// any external callers (e.g. tests that hand-craft literals) parse
/// numbers the same way — never affected by the host's locale.
/// </summary>
internal static class FleeNumber
{
    public static double Parse(string s) =>
        double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

    public static bool TryParse(string s, out double v) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
