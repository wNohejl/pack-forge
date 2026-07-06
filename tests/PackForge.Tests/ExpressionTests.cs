using PackForge.Core.Expressions;

namespace PackForge.Tests;

public class ExpressionTests
{
    private static double Eval(string formula, Dictionary<string, double>? env = null) =>
        ExpressionParser.Parse(formula).Evaluate(env ?? []);

    [Theory]
    [InlineData("1 + 2 * 3", 7)]
    [InlineData("(1 + 2) * 3", 9)]
    [InlineData("2 ^ 3 ^ 2", 512)] // right-associative
    [InlineData("-2 ^ 2", -4)]     // unary binds looser than ^
    [InlineData("10 / 4", 2.5)]
    [InlineData("min(3, max(1, 2))", 2)]
    [InlineData("sqrt(16) + abs(-2)", 6)]
    [InlineData("1.5e2 - 50", 100)]
    public void Evaluates_with_correct_precedence(string formula, double expected) =>
        Assert.Equal(expected, Eval(formula), precision: 10);

    [Fact]
    public void Resolves_variables_from_environment() =>
        Assert.Equal(21, Eval("a * b + 1", new() { ["a"] = 4, ["b"] = 5 }));

    [Theory]
    [InlineData("1 +")]
    [InlineData("(1 + 2")]
    [InlineData("2 ** 3")]
    [InlineData("1 $ 2")]
    public void Rejects_malformed_formulas(string formula) =>
        Assert.Throws<FormatException>(() => ExpressionParser.Parse(formula));

    [Fact]
    public void Unknown_symbol_throws_on_evaluation() =>
        Assert.Throws<KeyNotFoundException>(() => Eval("missing + 1"));

    [Fact]
    public void Wrong_arity_throws() =>
        Assert.Throws<ArgumentException>(() => Eval("min(1)"));
}
