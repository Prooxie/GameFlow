using System.Collections.Concurrent;
using System.Text;

namespace Autofire.Infrastructure.Theming.Flee;

/// <summary>
/// Recursive-descent parser for the trimmed Flee dialect supported by the
/// VSCView-compatible theme engine. Produces a <see cref="FleeNode"/> AST
/// that <see cref="FleeNode.Evaluate(IFleeSymbols)"/> can walk on the hot
/// path without ever allocating.
///
/// <para>
/// Grammar (lowest precedence first; same shape as VSCView's THEMEENGINE.md):
/// <code>
///   expr        := or-expr
///   or-expr     := and-expr ( ("or") and-expr )*
///   and-expr    := not-expr ( ("and") not-expr )*
///   not-expr    := "not" not-expr | cmp-expr
///   cmp-expr    := add-expr ( ("&lt;"|"&lt;="|"&gt;"|"&gt;="|"=="|"="|"!="|"&lt;&gt;") add-expr )*
///   add-expr    := mul-expr ( ("+"|"-") mul-expr )*
///   mul-expr    := unary    ( ("*"|"/") unary )*
///   unary       := "-" unary | primary
///   primary     := number | ident | ident "(" args ")" | "(" expr ")"
///   args        := expr ( "," expr )*
///   ident       := [A-Za-z_][A-Za-z_0-9.]*   (colon segments preserved verbatim)
/// </code>
/// </para>
///
/// <para>
/// Parsed expressions are interned in a process-wide cache keyed on the
/// raw source string. Two theme elements that reference the same formula
/// (e.g. several PBars all driven by <c>"triggers:l:analog"</c>) share
/// the same AST instance and the same allocation footprint. The cache is
/// bounded only by the number of unique formulas across all loaded
/// themes — in practice a few hundred entries at most.
/// </para>
/// </summary>
public static class FleeParser
{
    private static readonly ConcurrentDictionary<string, FleeNode> ParseCache = new();

    /// <summary>
    /// Returns the AST for <paramref name="source"/>, throwing
    /// <see cref="FleeParseException"/> if the formula is invalid.
    /// Whitespace at either end is ignored.
    /// </summary>
    public static FleeNode Parse(string source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var trimmed = source.Trim();
        if (trimmed.Length == 0)
        {
            // Empty formula behaves as the constant zero. Several VSCView
            // themes ship optional fields with empty strings to mean
            // "not driven"; treating empty as zero keeps them rendering
            // instead of crashing the host.
            return new LiteralNode(0);
        }

        return ParseCache.GetOrAdd(trimmed, ParseUncached);
    }

    /// <summary>
    /// Parses <paramref name="source"/> bypassing the cache. Used by the
    /// cache miss path above and by tests that want to assert allocation
    /// behaviour without inheriting cached entries from earlier cases.
    /// </summary>
    public static FleeNode ParseUncached(string source)
    {
        var tokens = Tokenize(source);
        var cursor = new Cursor(tokens, source);
        var expr = ParseOr(cursor);
        if (!cursor.AtEnd)
        {
            throw FleeParseException.At(cursor.Peek().Position, "end of expression", source);
        }
        return expr;
    }

    // ─── Recursive-descent productions ────────────────────────────────────────

    private static FleeNode ParseOr(Cursor c)
    {
        var node = ParseAnd(c);
        while (c.MatchKeyword("or"))
        {
            node = new BinaryNode("or", node, ParseAnd(c));
        }
        return node;
    }

    private static FleeNode ParseAnd(Cursor c)
    {
        var node = ParseNot(c);
        while (c.MatchKeyword("and"))
        {
            node = new BinaryNode("and", node, ParseNot(c));
        }
        return node;
    }

    private static FleeNode ParseNot(Cursor c)
    {
        if (c.MatchKeyword("not"))
        {
            return new UnaryNode("not", ParseNot(c));
        }
        return ParseComparison(c);
    }

    private static FleeNode ParseComparison(Cursor c)
    {
        var node = ParseAdd(c);
        while (true)
        {
            // Order matters: two-character operators must be tried before
            // their one-character prefixes so "<=" doesn't bind as "<" "=".
            if      (c.Match("<=")) { node = new BinaryNode("<=", node, ParseAdd(c)); }
            else if (c.Match(">=")) { node = new BinaryNode(">=", node, ParseAdd(c)); }
            else if (c.Match("==")) { node = new BinaryNode("==", node, ParseAdd(c)); }
            else if (c.Match("!=")) { node = new BinaryNode("!=", node, ParseAdd(c)); }
            else if (c.Match("<>")) { node = new BinaryNode("<>", node, ParseAdd(c)); }
            else if (c.Match("<"))  { node = new BinaryNode("<",  node, ParseAdd(c)); }
            else if (c.Match(">"))  { node = new BinaryNode(">",  node, ParseAdd(c)); }
            else if (c.Match("="))  { node = new BinaryNode("=",  node, ParseAdd(c)); }
            else                    { break; }
        }
        return node;
    }

    private static FleeNode ParseAdd(Cursor c)
    {
        var node = ParseMul(c);
        while (true)
        {
            if      (c.Match("+")) { node = new BinaryNode("+", node, ParseMul(c)); }
            else if (c.Match("-")) { node = new BinaryNode("-", node, ParseMul(c)); }
            else                    { break; }
        }
        return node;
    }

    private static FleeNode ParseMul(Cursor c)
    {
        var node = ParseUnary(c);
        while (true)
        {
            if      (c.Match("*")) { node = new BinaryNode("*", node, ParseUnary(c)); }
            else if (c.Match("/")) { node = new BinaryNode("/", node, ParseUnary(c)); }
            else                    { break; }
        }
        return node;
    }

    private static FleeNode ParseUnary(Cursor c)
    {
        if (c.Match("-"))
        {
            return new UnaryNode("-", ParseUnary(c));
        }
        if (c.Match("+"))
        {
            // Tolerate unary plus for symmetry; it's a no-op.
            return ParseUnary(c);
        }
        return ParsePrimary(c);
    }

    private static FleeNode ParsePrimary(Cursor c)
    {
        var tok = c.Peek();

        if (tok.Kind == TokenKind.Number)
        {
            c.Advance();
            return new LiteralNode(FleeNumber.Parse(tok.Text));
        }

        if (tok.Kind == TokenKind.Identifier)
        {
            c.Advance();

            // Boolean literals — case-insensitive per VSCView convention.
            if (string.Equals(tok.Text, "true",  StringComparison.OrdinalIgnoreCase)) { return new LiteralNode(1); }
            if (string.Equals(tok.Text, "false", StringComparison.OrdinalIgnoreCase)) { return new LiteralNode(0); }

            // Function call? Detected by an immediately-following '('.
            if (c.Match("("))
            {
                var args = new List<FleeNode>(4);
                if (!c.Match(")"))
                {
                    args.Add(ParseOr(c));
                    while (c.Match(","))
                    {
                        args.Add(ParseOr(c));
                    }
                    if (!c.Match(")"))
                    {
                        throw FleeParseException.At(c.Peek().Position, "')'", c.Source);
                    }
                }
                return new CallNode(tok.Text.ToLowerInvariant(), args);
            }

            // Plain variable reference. Colons in the identifier (e.g.
            // "stick_left:x") were already collected by the tokenizer.
            return new VariableNode(tok.Text);
        }

        if (c.Match("("))
        {
            var inner = ParseOr(c);
            if (!c.Match(")"))
            {
                throw FleeParseException.At(c.Peek().Position, "')'", c.Source);
            }
            return inner;
        }

        throw FleeParseException.At(tok.Position, "number, identifier or '('", c.Source);
    }

    // ─── Tokenizer ────────────────────────────────────────────────────────────

    private enum TokenKind { Number, Identifier, Punct, End }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    /// <summary>
    /// Splits <paramref name="source"/> into <see cref="Token"/> instances.
    /// Identifiers may contain letters, digits, underscores, dots (for the
    /// <c>math.</c> namespace) and colons (for VSCView's <c>l:analog</c>
    /// style variable suffixes). Whitespace separates tokens but is
    /// otherwise ignored.
    /// </summary>
    private static List<Token> Tokenize(string source)
    {
        var tokens = new List<Token>(16);
        var i = 0;
        while (i < source.Length)
        {
            var ch = source[i];
            if (char.IsWhiteSpace(ch)) { i++; continue; }

            if (char.IsDigit(ch) || (ch == '.' && i + 1 < source.Length && char.IsDigit(source[i + 1])))
            {
                var start = i;
                var sawDot = false;
                var sawExp = false;
                while (i < source.Length)
                {
                    var dc = source[i];
                    if (char.IsDigit(dc)) { i++; continue; }
                    if (dc == '.' && !sawDot && !sawExp) { sawDot = true; i++; continue; }
                    if ((dc == 'e' || dc == 'E') && !sawExp)
                    {
                        sawExp = true;
                        i++;
                        if (i < source.Length && (source[i] == '+' || source[i] == '-')) { i++; }
                        continue;
                    }
                    break;
                }
                tokens.Add(new Token(TokenKind.Number, source.Substring(start, i - start), start));
                continue;
            }

            if (char.IsLetter(ch) || ch == '_')
            {
                var start = i;
                while (i < source.Length)
                {
                    var ic = source[i];
                    if (char.IsLetterOrDigit(ic) || ic == '_' || ic == '.' || ic == ':')
                    {
                        i++;
                    }
                    else { break; }
                }
                tokens.Add(new Token(TokenKind.Identifier, source.Substring(start, i - start), start));
                continue;
            }

            // Two-char punctuation
            if (i + 1 < source.Length)
            {
                var pair = source.AsSpan(i, 2);
                if (pair.SequenceEqual("<=") || pair.SequenceEqual(">=") ||
                    pair.SequenceEqual("==") || pair.SequenceEqual("!=") ||
                    pair.SequenceEqual("<>"))
                {
                    tokens.Add(new Token(TokenKind.Punct, source.Substring(i, 2), i));
                    i += 2;
                    continue;
                }
            }

            switch (ch)
            {
                case '+': case '-': case '*': case '/':
                case '<': case '>': case '=':
                case '(': case ')':
                case ',':
                    tokens.Add(new Token(TokenKind.Punct, ch.ToString(), i));
                    i++;
                    continue;
                default:
                    throw FleeParseException.At(i, "valid character", source);
            }
        }
        tokens.Add(new Token(TokenKind.End, string.Empty, source.Length));
        return tokens;
    }

    /// <summary>
    /// Walking cursor over a token list. Match-style helpers consume the
    /// current token only when it matches the expected text/kind, so the
    /// productions read like plain BNF.
    /// </summary>
    private sealed class Cursor
    {
        private readonly List<Token> tokens;
        private int idx;

        public Cursor(List<Token> tokens, string source)
        {
            this.tokens = tokens;
            Source = source;
        }

        public string Source { get; }

        public bool AtEnd => tokens[idx].Kind == TokenKind.End;

        public Token Peek() => tokens[idx];

        public void Advance() => idx++;

        /// <summary>Consumes and returns true if the current token's text equals <paramref name="text"/>.</summary>
        public bool Match(string text)
        {
            var t = tokens[idx];
            if (t.Kind != TokenKind.End && t.Text == text)
            {
                idx++;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Consumes the current identifier token only if its text matches
        /// <paramref name="keyword"/> case-insensitively. Used for the
        /// English-word operators <c>and</c>, <c>or</c>, <c>not</c>.
        /// </summary>
        public bool MatchKeyword(string keyword)
        {
            var t = tokens[idx];
            if (t.Kind == TokenKind.Identifier &&
                string.Equals(t.Text, keyword, StringComparison.OrdinalIgnoreCase))
            {
                idx++;
                return true;
            }
            return false;
        }
    }

    // ─── Debug aid ────────────────────────────────────────────────────────────

    /// <summary>
    /// Reconstructs a canonical-spaced source representation of
    /// <paramref name="node"/>. Used in test failure messages so a
    /// mis-parsed AST shows up as a readable formula rather than a stack
    /// of record constructors.
    /// </summary>
    public static string Dump(FleeNode node)
    {
        var sb = new StringBuilder(64);
        Walk(node, sb);
        return sb.ToString();
    }

    private static void Walk(FleeNode node, StringBuilder sb)
    {
        switch (node)
        {
            case LiteralNode l: sb.Append(l.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)); break;
            case VariableNode v: sb.Append(v.Name); break;
            case UnaryNode u: sb.Append('(').Append(u.Op).Append(' '); Walk(u.Operand, sb); sb.Append(')'); break;
            case BinaryNode b: sb.Append('('); Walk(b.Left, sb); sb.Append(' ').Append(b.Op).Append(' '); Walk(b.Right, sb); sb.Append(')'); break;
            case CallNode c:
                sb.Append(c.Name).Append('(');
                for (var i = 0; i < c.Arguments.Count; i++)
                {
                    if (i > 0) { sb.Append(", "); }
                    Walk(c.Arguments[i], sb);
                }
                sb.Append(')');
                break;
        }
    }
}
