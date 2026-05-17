using Autofire.Infrastructure.Theming.Flee;
using Xunit;

namespace Autofire.Infrastructure.Tests.Theming;

/// <summary>
/// Tests for the Flee parser. Every theme that ships with Autofire — and
/// every theme a user drops in via the VSCView format — comes through
/// these productions, so the surface area covered here equals the
/// vocabulary actually authored in real theme.json files.
/// </summary>
public sealed class FleeParserTests
{
    /// <summary>
    /// Trivial literal — sanity-check that the tokenizer and the
    /// number-literal production agree on the most common form.
    /// </summary>
    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("1.5", 1.5)]
    [InlineData(".25", 0.25)]
    [InlineData("1e2", 100)]
    [InlineData("1.5e-1", 0.15)]
    public void Parses_numeric_literals(string src, double expected)
    {
        var node = FleeParser.ParseUncached(src);
        Assert.Equal(expected, node.Evaluate(NullSymbols.Instance));
    }

    /// <summary>
    /// Identifiers may contain colons and dots. The tokenizer must keep
    /// the whole sequence as one token so the resolver sees
    /// "stick_left:x" as a single name, not three.
    /// </summary>
    [Theory]
    [InlineData("stick_left:x")]
    [InlineData("triggers:l:analog")]
    [InlineData("quad_right:s")]
    [InlineData("touch_center:0:touch")]
    public void Parses_colon_qualified_variables(string src)
    {
        var node = FleeParser.ParseUncached(src);
        var variable = Assert.IsType<VariableNode>(node);
        Assert.Equal(src, variable.Name);
    }

    /// <summary>
    /// Standard arithmetic precedence: multiplication binds tighter than
    /// addition. <c>1 + 2 * 3</c> must equal 7, never 9.
    /// </summary>
    [Fact]
    public void Respects_arithmetic_precedence()
    {
        var node = FleeParser.ParseUncached("1 + 2 * 3");
        Assert.Equal(7, node.Evaluate(NullSymbols.Instance));
    }

    /// <summary>
    /// Parentheses override precedence in the obvious way. Without them
    /// this would parse as <c>1 + (2 * 3) - 4</c> = 3.
    /// </summary>
    [Fact]
    public void Respects_parenthesised_grouping()
    {
        var node = FleeParser.ParseUncached("(1 + 2) * (3 - 4)");
        Assert.Equal(-3, node.Evaluate(NullSymbols.Instance));
    }

    /// <summary>
    /// Unary minus must work both as a sign on a literal and as a
    /// prefix on a sub-expression. Catches a common bug where the
    /// parser swallows the minus into the number literal and then
    /// fails to handle "-(x+1)".
    /// </summary>
    [Theory]
    [InlineData("-1", -1)]
    [InlineData("-(1 + 2)", -3)]
    [InlineData("--1", 1)]
    public void Parses_unary_minus(string src, double expected)
    {
        var node = FleeParser.ParseUncached(src);
        Assert.Equal(expected, node.Evaluate(NullSymbols.Instance));
    }

    /// <summary>
    /// Boolean literals are case-insensitive, per VSCView convention.
    /// </summary>
    [Theory]
    [InlineData("true", 1)]
    [InlineData("TRUE", 1)]
    [InlineData("false", 0)]
    [InlineData("False", 0)]
    public void Parses_boolean_literals(string src, double expected)
    {
        var node = FleeParser.ParseUncached(src);
        Assert.Equal(expected, node.Evaluate(NullSymbols.Instance));
    }

    /// <summary>
    /// English-word operators bind less tightly than comparison, which
    /// binds less tightly than arithmetic. <c>x > 0 and y > 0</c> must
    /// parse as <c>(x > 0) and (y > 0)</c>, not <c>x > (0 and y) > 0</c>.
    /// </summary>
    [Fact]
    public void Binds_and_below_comparison()
    {
        var node = FleeParser.ParseUncached("1 > 0 and 2 > 0");
        Assert.Equal(1, node.Evaluate(NullSymbols.Instance));
    }

    /// <summary>
    /// "not" is right-associative and binds tighter than "and"/"or".
    /// </summary>
    [Theory]
    [InlineData("not 0", 1)]
    [InlineData("not 1", 0)]
    [InlineData("not 0 and 1", 1)]
    [InlineData("not (0 and 1)", 1)]
    public void Parses_not(string src, double expected)
    {
        var node = FleeParser.ParseUncached(src);
        Assert.Equal(expected, node.Evaluate(NullSymbols.Instance));
    }

    /// <summary>
    /// Function calls — variable-arity max/min, fixed-arity if/tobool,
    /// the math.* dispatch, and the bare-name <c>math.pi</c> /
    /// <c>math.e</c> constants that VSCView themes use without parens.
    /// </summary>
    [Theory]
    [InlineData("max(1, 2, 3)", 3)]
    [InlineData("min(1, 2, -1, 0)", -1)]
    [InlineData("if(1, 10, 20)", 10)]
    [InlineData("if(0, 10, 20)", 20)]
    [InlineData("tobool(0)", 0)]
    [InlineData("tobool(0.5)", 1)]
    [InlineData("math.abs(-3)", 3)]
    [InlineData("math.max(1.5, 2.5)", 2.5)]
    [InlineData("math.pow(2, 3)", 8)]
    [InlineData("math.pi", Math.PI)]
    [InlineData("math.e",  Math.E)]
    public void Parses_function_calls(string src, double expected)
    {
        var node = FleeParser.ParseUncached(src);
        Assert.Equal(expected, node.Evaluate(NullSymbols.Instance), precision: 10);
    }

    /// <summary>
    /// VSCView's THEMEENGINE.md example: invert a trigger stage signal.
    /// The whole formula must round-trip through the parser without
    /// allocations or surprises.
    /// </summary>
    [Fact]
    public void Parses_vscview_example_invert()
    {
        var node = FleeParser.ParseUncached("not tobool(triggers:l:stage2)");
        Assert.Equal(1, node.Evaluate(NullSymbols.Instance));
    }

    /// <summary>
    /// Garbage input fails loudly at parse time so theme authors see
    /// the error in the log file the moment they save, not three weeks
    /// later when a user reports "the trigger doesn't move".
    /// </summary>
    [Theory]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("max(1,")]
    [InlineData("@@")]
    public void Throws_on_malformed_input(string src)
    {
        _ = Assert.Throws<FleeParseException>(() => FleeParser.ParseUncached(src));
    }

    /// <summary>
    /// Symbol resolver that returns 0 for every name, to support tests
    /// of pure-arithmetic expressions without a real controller state.
    /// </summary>
    private sealed class NullSymbols : IFleeSymbols
    {
        public static readonly NullSymbols Instance = new();
        public double Resolve(string name) => 0;
    }
}
