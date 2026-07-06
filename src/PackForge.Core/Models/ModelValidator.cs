using PackForge.Core.Expressions;

namespace PackForge.Core.Models;

public readonly record struct ValidationError(string Target, string Message);

public static class ModelValidator
{
    /// <summary>
    /// Validates names, syntax, and symbol resolution. Expressions are evaluated in
    /// order, so an expression may reference parameters and any *earlier* expression;
    /// a forward reference is an unknown symbol (which also rules out cycles).
    /// </summary>
    public static List<ValidationError> Validate(ModelDefinition model)
    {
        var errors = new List<ValidationError>();

        if (string.IsNullOrWhiteSpace(model.Name))
            errors.Add(new("name", "Model name is required."));

        foreach (var (key, value) in model.Parameters)
        {
            if (string.IsNullOrWhiteSpace(key))
                errors.Add(new("parameters", "Parameter names must be non-empty."));
            if (!double.IsFinite(value))
                errors.Add(new($"parameters.{key}", "Parameter values must be finite numbers."));
        }

        if (model.Expressions.Count == 0)
            errors.Add(new("expressions", "At least one expression is required."));

        var known = new HashSet<string>(model.Parameters.Keys, StringComparer.Ordinal);
        var seenExpressionNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var expr in model.Expressions)
        {
            if (string.IsNullOrWhiteSpace(expr.Name))
            {
                errors.Add(new("expressions", "Expression names must be non-empty."));
                continue;
            }
            if (model.Parameters.ContainsKey(expr.Name) || !seenExpressionNames.Add(expr.Name))
                errors.Add(new(expr.Name, $"Duplicate name '{expr.Name}' — parameters and expressions share one namespace."));

            ExprNode node;
            try
            {
                node = ExpressionParser.Parse(expr.Formula);
            }
            catch (FormatException ex)
            {
                errors.Add(new(expr.Name, $"Syntax error: {ex.Message}"));
                known.Add(expr.Name);
                continue;
            }

            foreach (var id in node.Identifiers().Distinct())
            {
                if (!known.Contains(id))
                    errors.Add(new(expr.Name, $"Unknown symbol '{id}' — only parameters and earlier expressions are in scope."));
            }

            known.Add(expr.Name);
        }

        return errors;
    }
}

public static class ModelEvaluator
{
    /// <summary>Evaluates expressions in order. Assumes the model already validated.</summary>
    public static Dictionary<string, double> Evaluate(ModelDefinition model)
    {
        var env = new Dictionary<string, double>(model.Parameters, StringComparer.Ordinal);
        var results = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var expr in model.Expressions)
        {
            var value = ExpressionParser.Parse(expr.Formula).Evaluate(env);
            env[expr.Name] = value;
            results[expr.Name] = value;
        }
        return results;
    }
}
