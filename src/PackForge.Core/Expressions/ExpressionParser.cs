using System.Globalization;

namespace PackForge.Core.Expressions;

/// <summary>
/// Recursive-descent parser for the constrained formula language:
/// numbers, identifiers, + - * / ^, unary minus, parentheses, and a fixed
/// function whitelist. Deliberately no assignment, no calls beyond the
/// whitelist — user math stays data, not code.
/// </summary>
public static class ExpressionParser
{
    public static readonly IReadOnlyDictionary<string, int> Functions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["sqrt"] = 1, ["abs"] = 1, ["sin"] = 1, ["cos"] = 1, ["tan"] = 1,
        ["exp"] = 1, ["log"] = 1, ["log10"] = 1, ["floor"] = 1, ["ceil"] = 1,
        ["min"] = 2, ["max"] = 2, ["pow"] = 2,
    };

    public static ExprNode Parse(string formula)
    {
        var tokens = Tokenize(formula);
        var pos = 0;
        var node = ParseAddSub(tokens, ref pos);
        if (pos != tokens.Count)
            throw new FormatException($"Unexpected token '{tokens[pos].Text}' at position {tokens[pos].Position}.");
        return node;
    }

    // ---- grammar: addsub -> muldiv (('+'|'-') muldiv)* ; muldiv -> unary (('*'|'/') unary)* ;
    //      unary -> '-' unary | power ; power -> primary ('^' unary)? ;
    //      primary -> number | ident | ident '(' args ')' | '(' addsub ')'
    // Unary minus binds looser than '^' (math convention: -2^2 = -4); '^' is right-associative.

    private static ExprNode ParseAddSub(List<Token> t, ref int pos)
    {
        var left = ParseMulDiv(t, ref pos);
        while (pos < t.Count && t[pos].Text is "+" or "-")
        {
            var op = t[pos++].Text;
            left = new BinaryNode(op, left, ParseMulDiv(t, ref pos));
        }
        return left;
    }

    private static ExprNode ParseMulDiv(List<Token> t, ref int pos)
    {
        var left = ParseUnary(t, ref pos);
        while (pos < t.Count && t[pos].Text is "*" or "/")
        {
            var op = t[pos++].Text;
            left = new BinaryNode(op, left, ParseUnary(t, ref pos));
        }
        return left;
    }

    private static ExprNode ParseUnary(List<Token> t, ref int pos)
    {
        if (pos < t.Count && t[pos].Text == "-")
        {
            pos++;
            return new UnaryNode(ParseUnary(t, ref pos));
        }
        return ParsePower(t, ref pos);
    }

    private static ExprNode ParsePower(List<Token> t, ref int pos)
    {
        var left = ParsePrimary(t, ref pos);
        if (pos < t.Count && t[pos].Text == "^")
        {
            pos++;
            return new BinaryNode("^", left, ParseUnary(t, ref pos)); // right-assoc; also allows 2^-3
        }
        return left;
    }

    private static ExprNode ParsePrimary(List<Token> t, ref int pos)
    {
        if (pos >= t.Count)
            throw new FormatException("Unexpected end of formula.");
        var tok = t[pos];

        if (tok.Kind == TokenKind.Number)
        {
            pos++;
            return new NumberNode(double.Parse(tok.Text, CultureInfo.InvariantCulture));
        }

        if (tok.Kind == TokenKind.Identifier)
        {
            pos++;
            if (pos < t.Count && t[pos].Text == "(")
            {
                pos++; // '('
                var args = new List<ExprNode>();
                if (pos < t.Count && t[pos].Text != ")")
                {
                    args.Add(ParseAddSub(t, ref pos));
                    while (pos < t.Count && t[pos].Text == ",")
                    {
                        pos++;
                        args.Add(ParseAddSub(t, ref pos));
                    }
                }
                Expect(t, ref pos, ")");
                return new FunctionNode(tok.Text, args);
            }
            return new VariableNode(tok.Text);
        }

        if (tok.Text == "(")
        {
            pos++;
            var inner = ParseAddSub(t, ref pos);
            Expect(t, ref pos, ")");
            return inner;
        }

        throw new FormatException($"Unexpected token '{tok.Text}' at position {tok.Position}.");
    }

    private static void Expect(List<Token> t, ref int pos, string text)
    {
        if (pos >= t.Count || t[pos].Text != text)
            throw new FormatException($"Expected '{text}' at position {(pos < t.Count ? t[pos].Position : -1)}.");
        pos++;
    }

    // ---- tokenizer ----

    private enum TokenKind { Number, Identifier, Symbol }

    private readonly record struct Token(TokenKind Kind, string Text, int Position);

    private static List<Token> Tokenize(string s)
    {
        var tokens = new List<Token>();
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsDigit(c) || c == '.')
            {
                var start = i;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E'
                       || ((s[i] == '+' || s[i] == '-') && i > start && (s[i - 1] == 'e' || s[i - 1] == 'E'))))
                    i++;
                tokens.Add(new Token(TokenKind.Number, s[start..i], start));
            }
            else if (char.IsLetter(c) || c == '_')
            {
                var start = i;
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_'))
                    i++;
                tokens.Add(new Token(TokenKind.Identifier, s[start..i], start));
            }
            else if ("+-*/^(),".Contains(c))
            {
                tokens.Add(new Token(TokenKind.Symbol, c.ToString(), i));
                i++;
            }
            else
            {
                throw new FormatException($"Invalid character '{c}' at position {i}.");
            }
        }
        return tokens;
    }
}

public abstract record ExprNode
{
    public abstract double Evaluate(IReadOnlyDictionary<string, double> env);

    /// <summary>All identifiers this expression reads (variables, not functions).</summary>
    public abstract IEnumerable<string> Identifiers();
}

public sealed record NumberNode(double Value) : ExprNode
{
    public override double Evaluate(IReadOnlyDictionary<string, double> env) => Value;
    public override IEnumerable<string> Identifiers() => [];
}

public sealed record VariableNode(string Name) : ExprNode
{
    public override double Evaluate(IReadOnlyDictionary<string, double> env) =>
        env.TryGetValue(Name, out var v) ? v : throw new KeyNotFoundException($"Unknown symbol '{Name}'.");
    public override IEnumerable<string> Identifiers() => [Name];
}

public sealed record UnaryNode(ExprNode Operand) : ExprNode
{
    public override double Evaluate(IReadOnlyDictionary<string, double> env) => -Operand.Evaluate(env);
    public override IEnumerable<string> Identifiers() => Operand.Identifiers();
}

public sealed record BinaryNode(string Op, ExprNode Left, ExprNode Right) : ExprNode
{
    public override double Evaluate(IReadOnlyDictionary<string, double> env)
    {
        var l = Left.Evaluate(env);
        var r = Right.Evaluate(env);
        return Op switch
        {
            "+" => l + r,
            "-" => l - r,
            "*" => l * r,
            "/" => l / r,
            "^" => Math.Pow(l, r),
            _ => throw new InvalidOperationException($"Unknown operator '{Op}'."),
        };
    }

    public override IEnumerable<string> Identifiers() => Left.Identifiers().Concat(Right.Identifiers());
}

public sealed record FunctionNode(string Name, IReadOnlyList<ExprNode> Args) : ExprNode
{
    public override double Evaluate(IReadOnlyDictionary<string, double> env)
    {
        if (!ExpressionParser.Functions.TryGetValue(Name, out var arity))
            throw new KeyNotFoundException($"Unknown function '{Name}'.");
        if (Args.Count != arity)
            throw new ArgumentException($"Function '{Name}' expects {arity} argument(s), got {Args.Count}.");

        var a = Args.Select(x => x.Evaluate(env)).ToArray();
        return Name.ToLowerInvariant() switch
        {
            "sqrt" => Math.Sqrt(a[0]),
            "abs" => Math.Abs(a[0]),
            "sin" => Math.Sin(a[0]),
            "cos" => Math.Cos(a[0]),
            "tan" => Math.Tan(a[0]),
            "exp" => Math.Exp(a[0]),
            "log" => Math.Log(a[0]),
            "log10" => Math.Log10(a[0]),
            "floor" => Math.Floor(a[0]),
            "ceil" => Math.Ceiling(a[0]),
            "min" => Math.Min(a[0], a[1]),
            "max" => Math.Max(a[0], a[1]),
            "pow" => Math.Pow(a[0], a[1]),
            _ => throw new KeyNotFoundException($"Unknown function '{Name}'."),
        };
    }

    public override IEnumerable<string> Identifiers() => Args.SelectMany(x => x.Identifiers());
}
