using System.IO.Compression;
using System.Security.Cryptography;
using PackForge.Core.Models;
using PackForge.Core.Packaging;

namespace PackForge.Tests;

public class PackageBuilderTests
{
    private static ModelDefinition Model() => ModelDefinition.FromJson("""
        {
          "name": "demo",
          "parameters": { "x": 2 },
          "expressions": [ { "name": "y", "formula": "x ^ 10" } ]
        }
        """);

    [Fact]
    public void Same_inputs_build_byte_identical_packages()
    {
        var a = PackageBuilder.Build(Model(), "abc123", 1);
        var b = PackageBuilder.Build(Model(), "abc123", 1);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(a)), Convert.ToHexString(SHA256.HashData(b)));
    }

    [Fact]
    public void Different_version_changes_the_bytes()
    {
        var v1 = PackageBuilder.Build(Model(), "abc123", 1);
        var v2 = PackageBuilder.Build(Model(), "abc123", 2);
        Assert.NotEqual(Convert.ToHexString(SHA256.HashData(v1)), Convert.ToHexString(SHA256.HashData(v2)));
    }

    [Fact]
    public void Package_contains_manifest_inputs_results_and_sbom()
    {
        using var zip = new ZipArchive(new MemoryStream(PackageBuilder.Build(Model(), "abc123", 1)), ZipArchiveMode.Read);
        Assert.Equal(["manifest.json", "inputs/model.json", "outputs/results.json", "sbom.json"], zip.Entries.Select(e => e.FullName).ToArray());

        using var reader = new StreamReader(zip.GetEntry("outputs/results.json")!.Open());
        Assert.Contains("1024", reader.ReadToEnd()); // 2^10
    }

    [Fact]
    public void Sbom_lists_contents_with_sha256_digests()
    {
        using var zip = new ZipArchive(new MemoryStream(PackageBuilder.Build(Model(), "abc123", 1)), ZipArchiveMode.Read);
        using var reader = new StreamReader(zip.GetEntry("sbom.json")!.Open());
        var sbom = reader.ReadToEnd();
        Assert.Contains("CycloneDX", sbom);
        Assert.Contains("outputs/results.json", sbom);
        Assert.Contains("SHA-256", sbom);
    }
}
