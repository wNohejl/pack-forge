using PackForge.Core.Expressions;
using PackForge.Core.Models;

namespace PackForge.Tests;

public class NativeKernelTests
{
    private static ModelDefinition Model() => ModelDefinition.FromJson("""
        {
          "name": "kernel",
          "parameters": { "principal": 10000, "rate": 0.07, "years": 30 },
          "expressions": [
            { "name": "growth", "formula": "(1 + rate) ^ years" },
            { "name": "future", "formula": "principal * growth" },
            { "name": "mixed", "formula": "sqrt(future) + min(rate, 1) - max(years, 10)" }
          ]
        }
        """);

    [Fact]
    public void Native_kernel_is_available_in_test_output()
    {
        // The g++-built DLL is copied next to the tests; if this fails the native
        // build/copy pipeline regressed (managed fallback would still be correct).
        Assert.True(NativeMathKernel.IsAvailable, "packforge_eval native kernel did not load.");
    }

    [Fact]
    public void Native_and_managed_evaluators_agree()
    {
        var managed = ModelEvaluator.Evaluate(Model());
        var native = NativeModelEvaluator.Evaluate(Model());

        Assert.Equal(managed.Keys.OrderBy(k => k), native.Keys.OrderBy(k => k));
        foreach (var key in managed.Keys)
            Assert.Equal(managed[key], native[key], precision: 9); // agree to well within the 12-sig-fig package rounding
    }

    [Fact]
    public void Rpn_compiler_roundtrips_through_the_kernel()
    {
        var ast = ExpressionParser.Parse("2 ^ 10 + sqrt(144)");
        var program = RpnCompiler.Compile(ast, new Dictionary<string, int>());
        Assert.Equal(1036, NativeMathKernel.Evaluate(program, []), precision: 9); // 1024 + 12
    }
}
