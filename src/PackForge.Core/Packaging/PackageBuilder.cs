using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PackForge.Core.Expressions;
using PackForge.Core.Models;

namespace PackForge.Core.Packaging;

/// <summary>
/// Builds the deployment package: a zip that is byte-for-byte reproducible for the
/// same (model, version) — fixed entry timestamps, fixed entry order, no wall-clock
/// data in the manifest. Reproducibility is the release-engineering guarantee:
/// identical inputs must yield an identical checksum.
///
/// The numeric core runs in the native C++ kernel when available (else managed).
/// Results are rounded to 12 significant digits before serialization so the package
/// is reproducible regardless of which evaluator ran — absorbing sub-ULP differences
/// between std::pow/Math.Pow etc.
/// </summary>
public static class PackageBuilder
{
    private static readonly DateTimeOffset FixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static byte[] Build(ModelDefinition model, string modelSha256, int version)
    {
        var raw = NativeModelEvaluator.Evaluate(model);
        var results = raw.ToDictionary(kv => kv.Key, kv => RoundSignificant(kv.Value, 12));

        var manifest = new
        {
            schema = 1,
            generator = "PackForge",
            name = model.Name,
            version,
            modelSha256,
            parameterCount = model.Parameters.Count,
            expressionCount = model.Expressions.Count,
        };

        var modelJson = model.ToJson();
        var resultsJson = JsonSerializer.Serialize(results, JsonOptions);
        var manifestJson = JsonSerializer.Serialize(manifest, JsonOptions);

        // Supply-chain: every package carries a CycloneDX-style SBOM of its own
        // contents with SHA-256 digests, so a consumer can verify what's inside.
        var sbom = new
        {
            bomFormat = "CycloneDX",
            specVersion = "1.5",
            components = new[]
            {
                Component("manifest.json", manifestJson),
                Component("inputs/model.json", modelJson),
                Component("outputs/results.json", resultsJson),
            },
        };

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(zip, "manifest.json", manifestJson);
            AddEntry(zip, "inputs/model.json", modelJson);
            AddEntry(zip, "outputs/results.json", resultsJson);
            AddEntry(zip, "sbom.json", JsonSerializer.Serialize(sbom, JsonOptions));
        }
        return ms.ToArray();
    }

    private static object Component(string name, string content) => new
    {
        type = "file",
        name,
        hashes = new[] { new { alg = "SHA-256", content = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(content))) } },
    };

    private static void AddEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = FixedTimestamp;
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    /// <summary>Round to N significant digits so results are stable across evaluators.</summary>
    private static double RoundSignificant(double value, int digits)
    {
        if (value == 0 || !double.IsFinite(value))
            return value;
        var scale = Math.Pow(10, digits - 1 - (int)Math.Floor(Math.Log10(Math.Abs(value))));
        return Math.Round(value * scale) / scale;
    }
}
