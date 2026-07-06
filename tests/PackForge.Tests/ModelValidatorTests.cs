using PackForge.Core.Models;

namespace PackForge.Tests;

public class ModelValidatorTests
{
    private static ModelDefinition ValidModel() => new()
    {
        Name = "m",
        Parameters = { ["x"] = 2 },
        Expressions =
        {
            new() { Name = "y", Formula = "x * 3" },
            new() { Name = "z", Formula = "y + x" },
        },
    };

    [Fact]
    public void Valid_model_has_no_errors() => Assert.Empty(ModelValidator.Validate(ValidModel()));

    [Fact]
    public void Unknown_symbol_is_reported_with_expression_name()
    {
        var m = ValidModel();
        m.Expressions[0].Formula = "nope * 2";
        var errors = ModelValidator.Validate(m);
        Assert.Contains(errors, e => e.Target == "y" && e.Message.Contains("nope"));
    }

    [Fact]
    public void Forward_reference_is_unknown_symbol()
    {
        var m = ValidModel();
        m.Expressions[0].Formula = "z + 1"; // z defined later
        Assert.Contains(ModelValidator.Validate(m), e => e.Target == "y" && e.Message.Contains("'z'"));
    }

    [Fact]
    public void Duplicate_names_are_rejected()
    {
        var m = ValidModel();
        m.Expressions[1].Name = "y";
        Assert.Contains(ModelValidator.Validate(m), e => e.Message.Contains("Duplicate"));
    }

    [Fact]
    public void Syntax_errors_are_reported()
    {
        var m = ValidModel();
        m.Expressions[0].Formula = "x * ";
        Assert.Contains(ModelValidator.Validate(m), e => e.Target == "y" && e.Message.StartsWith("Syntax error"));
    }

    [Fact]
    public void Evaluation_runs_in_order()
    {
        var results = ModelEvaluator.Evaluate(ValidModel());
        Assert.Equal(6, results["y"]);
        Assert.Equal(8, results["z"]);
    }
}
