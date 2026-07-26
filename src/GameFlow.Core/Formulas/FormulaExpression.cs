using System.Globalization;

namespace GameFlow.Core.Formulas;

/// <summary>
/// Compiles a small arithmetic/logic expression over a rule's source
/// values into a delegate evaluated per tick with zero allocation.
/// This is the engine under the Formula combine mode (and the future
/// drag-and-drop editor, whose blocks serialize to exactly these
/// strings).
///
/// <para>Language:</para>
/// <list type="bullet">
///   <item><c>s1</c>..<c>sN</c> — the rule's sources, in list order (1-based).</item>
///   <item>Arithmetic: <c>+ - * /</c>, unary <c>-</c>, parentheses. Division by zero yields 0 rather than infinity.</item>
///   <item>Comparison: <c>&lt; &gt; &lt;= &gt;= == !=</c> — result is 1 or 0.</item>
///   <item>Logic: <c>and or not</c> (also <c>&amp;&amp; || !</c>) — nonzero is true; result is 1 or 0.</item>
///   <item>Functions: <c>if(cond, a, b)</c>, <c>min(a, b, ...)</c>, <c>max(a, b, ...)</c>, <c>abs(x)</c>, <c>clamp(x, lo, hi)</c>.</item>
/// </list>
///
/// <para>
/// Compilation is strict: an unknown identifier, a source index past the
/// rule's actual source count, or a syntax error all FAIL the compile
/// (returning null + a reason) rather than silently evaluating to 0 —
/// a typo like <c>s3</c> on a two-source rule should be caught, not
/// become a mystery dead input.
/// </para>
/// </summary>
public static class FormulaExpression
{
    /// <summary>
    /// Compiles <paramref name="expression"/> against a rule with
    /// <paramref name="sourceCount"/> sources. Returns the evaluator, or
    /// null with <paramref name="error"/> describing the first problem.
    /// </summary>
    public static Func<float[], float>? Compile(string expression, int sourceCount, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Formula is empty.";
            return null;
        }

        try
        {
            var parser = new Parser(expression, sourceCount);
            var body = parser.ParseExpression();
            parser.ExpectEnd();
            return body;
        }
        catch (FormulaParseException ex)
        {
            error = ex.Message;
            return null;
        }
    }

    private sealed class FormulaParseException(string message) : Exception(message);

    private sealed class Parser
    {
        private readonly string text;
        private readonly int sourceCount;
        private int position;

        public Parser(string text, int sourceCount)
        {
            this.text = text;
            this.sourceCount = sourceCount;
        }

        public Func<float[], float> ParseExpression() => ParseOr();

        public void ExpectEnd()
        {
            SkipWhitespace();
            if (position < text.Length)
            {
                throw new FormulaParseException($"Unexpected '{text[position]}' at position {position}.");
            }
        }

        private Func<float[], float> ParseOr()
        {
            var left = ParseAnd();
            while (TryConsumeKeyword("or") || TryConsumeSymbol("||"))
            {
                var right = ParseAnd();
                var l = left;
                left = s => l(s) != 0f || right(s) != 0f ? 1f : 0f;
            }
            return left;
        }

        private Func<float[], float> ParseAnd()
        {
            var left = ParseNot();
            while (TryConsumeKeyword("and") || TryConsumeSymbol("&&"))
            {
                var right = ParseNot();
                var l = left;
                left = s => l(s) != 0f && right(s) != 0f ? 1f : 0f;
            }
            return left;
        }

        private Func<float[], float> ParseNot()
        {
            if (TryConsumeKeyword("not") || TryConsumeSymbol("!"))
            {
                // "!" must not swallow "!=" — TryConsumeSymbol("!") below
                // is only reached when the NEXT char isn't '=' (checked
                // inside), so "!=" still parses as a comparison operator.
                var operand = ParseNot();
                return s => operand(s) != 0f ? 0f : 1f;
            }
            return ParseComparison();
        }

        private Func<float[], float> ParseComparison()
        {
            var left = ParseAdditive();
            SkipWhitespace();

            string? op = null;
            foreach (var candidate in (string[])["<=", ">=", "==", "!=", "<", ">"])
            {
                if (Peek(candidate))
                {
                    op = candidate;
                    position += candidate.Length;
                    break;
                }
            }
            if (op is null)
            {
                return left;
            }

            var right = ParseAdditive();
            return op switch
            {
                "<" => s => left(s) < right(s) ? 1f : 0f,
                ">" => s => left(s) > right(s) ? 1f : 0f,
                "<=" => s => left(s) <= right(s) ? 1f : 0f,
                ">=" => s => left(s) >= right(s) ? 1f : 0f,
                "==" => s => left(s) == right(s) ? 1f : 0f,
                _ => s => left(s) != right(s) ? 1f : 0f,
            };
        }

        private Func<float[], float> ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                SkipWhitespace();
                if (TryConsumeSymbol("+"))
                {
                    var right = ParseMultiplicative();
                    var l = left;
                    left = s => l(s) + right(s);
                }
                else if (Peek("-") && !Peek("->"))
                {
                    position++;
                    var right = ParseMultiplicative();
                    var l = left;
                    left = s => l(s) - right(s);
                }
                else
                {
                    return left;
                }
            }
        }

        private Func<float[], float> ParseMultiplicative()
        {
            var left = ParseUnary();
            while (true)
            {
                SkipWhitespace();
                if (TryConsumeSymbol("*"))
                {
                    var right = ParseUnary();
                    var l = left;
                    left = s => l(s) * right(s);
                }
                else if (TryConsumeSymbol("/"))
                {
                    var right = ParseUnary();
                    var l = left;
                    left = s =>
                    {
                        var denominator = right(s);
                        return denominator == 0f ? 0f : l(s) / denominator;
                    };
                }
                else
                {
                    return left;
                }
            }
        }

        private Func<float[], float> ParseUnary()
        {
            SkipWhitespace();
            if (TryConsumeSymbol("-"))
            {
                var operand = ParseUnary();
                return s => -operand(s);
            }
            return ParsePrimary();
        }

        private Func<float[], float> ParsePrimary()
        {
            SkipWhitespace();
            if (position >= text.Length)
            {
                throw new FormulaParseException("Unexpected end of formula.");
            }

            var c = text[position];

            if (c == '(')
            {
                position++;
                var inner = ParseOr();
                Expect(')');
                return inner;
            }

            if (char.IsDigit(c) || c == '.')
            {
                return ParseNumber();
            }

            if (char.IsLetter(c) || c == '_')
            {
                return ParseIdentifier();
            }

            throw new FormulaParseException($"Unexpected '{c}' at position {position}.");
        }

        private Func<float[], float> ParseNumber()
        {
            var start = position;
            while (position < text.Length && (char.IsDigit(text[position]) || text[position] == '.'))
            {
                position++;
            }
            var slice = text[start..position];
            if (!float.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                throw new FormulaParseException($"Invalid number '{slice}' at position {start}.");
            }
            return _ => value;
        }

        private Func<float[], float> ParseIdentifier()
        {
            var start = position;
            while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
            {
                position++;
            }
            var name = text[start..position].ToLowerInvariant();

            // Source variable: s1..sN.
            if (name.Length >= 2 && name[0] == 's' && int.TryParse(name[1..], out var index))
            {
                if (index < 1 || index > sourceCount)
                {
                    throw new FormulaParseException(
                        $"'{name}' references source {index}, but this rule has {sourceCount} source(s).");
                }
                var zeroBased = index - 1;
                return s => zeroBased < s.Length ? s[zeroBased] : 0f;
            }

            // Function call?
            SkipWhitespace();
            if (position < text.Length && text[position] == '(')
            {
                position++;
                var args = new List<Func<float[], float>>();
                SkipWhitespace();
                if (position < text.Length && text[position] != ')')
                {
                    args.Add(ParseOr());
                    SkipWhitespace();
                    while (position < text.Length && text[position] == ',')
                    {
                        position++;
                        args.Add(ParseOr());
                        SkipWhitespace();
                    }
                }
                Expect(')');
                return BuildFunction(name, args);
            }

            throw new FormulaParseException($"Unknown identifier '{name}'. Sources are s1..s{sourceCount}.");
        }

        private static Func<float[], float> BuildFunction(string name, List<Func<float[], float>> args)
        {
            switch (name)
            {
                case "if":
                    RequireArity(name, args, 3);
                    return s => args[0](s) != 0f ? args[1](s) : args[2](s);

                case "min":
                    RequireAtLeast(name, args, 2);
                    return s =>
                    {
                        var value = args[0](s);
                        for (var i = 1; i < args.Count; i++)
                        {
                            value = MathF.Min(value, args[i](s));
                        }
                        return value;
                    };

                case "max":
                    RequireAtLeast(name, args, 2);
                    return s =>
                    {
                        var value = args[0](s);
                        for (var i = 1; i < args.Count; i++)
                        {
                            value = MathF.Max(value, args[i](s));
                        }
                        return value;
                    };

                case "abs":
                    RequireArity(name, args, 1);
                    return s => MathF.Abs(args[0](s));

                case "clamp":
                    RequireArity(name, args, 3);
                    return s => Math.Clamp(args[0](s), args[1](s), args[2](s));

                default:
                    throw new FormulaParseException(
                        $"Unknown function '{name}'. Available: if, min, max, abs, clamp.");
            }
        }

        private static void RequireArity(string name, List<Func<float[], float>> args, int count)
        {
            if (args.Count != count)
            {
                throw new FormulaParseException($"{name}(...) takes exactly {count} argument(s), got {args.Count}.");
            }
        }

        private static void RequireAtLeast(string name, List<Func<float[], float>> args, int count)
        {
            if (args.Count < count)
            {
                throw new FormulaParseException($"{name}(...) needs at least {count} arguments, got {args.Count}.");
            }
        }

        // ── Lexing helpers ─────────────────────────────────────────────

        private void SkipWhitespace()
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }
        }

        private bool Peek(string symbol)
        {
            return position + symbol.Length <= text.Length
                && string.CompareOrdinal(text, position, symbol, 0, symbol.Length) == 0;
        }

        private bool TryConsumeSymbol(string symbol)
        {
            SkipWhitespace();
            if (symbol == "!" && Peek("!="))
            {
                return false; // comparison operator, not logical not
            }
            if (!Peek(symbol))
            {
                return false;
            }
            position += symbol.Length;
            return true;
        }

        private bool TryConsumeKeyword(string keyword)
        {
            SkipWhitespace();
            if (position + keyword.Length > text.Length)
            {
                return false;
            }
            if (string.Compare(text, position, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0)
            {
                return false;
            }
            // Must be a whole word — "orange" is an identifier, not "or".
            var after = position + keyword.Length;
            if (after < text.Length && (char.IsLetterOrDigit(text[after]) || text[after] == '_'))
            {
                return false;
            }
            position += keyword.Length;
            return true;
        }

        private void Expect(char expected)
        {
            SkipWhitespace();
            if (position >= text.Length || text[position] != expected)
            {
                throw new FormulaParseException($"Expected '{expected}' at position {position}.");
            }
            position++;
        }
    }
}
