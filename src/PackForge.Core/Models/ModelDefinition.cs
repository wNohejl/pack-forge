using System.Text.Json;
using System.Text.Json.Serialization;

namespace PackForge.Core.Models;

/// <summary>
/// The "uploaded math": a constrained model format — named parameters plus a list of
/// expressions evaluated in order. No arbitrary code, so no sandboxing problem.
/// </summary>
public class ModelDefinition
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, double> Parameters { get; set; } = [];
    public List<ModelExpression> Expressions { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static ModelDefinition FromJson(string json) =>
        JsonSerializer.Deserialize<ModelDefinition>(json, JsonOptions)
        ?? throw new JsonException("Model JSON deserialized to null.");

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}

public class ModelExpression
{
    public string Name { get; set; } = string.Empty;
    public string Formula { get; set; } = string.Empty;
}
