using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace Forge.Catalog.Tests;

public class CatalogPublishTests
{
    [Fact]
    public void PublishedCatalogSignatureVerifiesAgainstItsOwnPublisherKey()
    {
        var keyPair = CatalogTestHelper.NewKeyPair();
        var json = CatalogTestHelper.Publish(
            keyPair.PrivateKeyBase64,
            CatalogTestHelper.Artifact("customer-profiles", "1.0.0", "CREATE TABLE customer_profile (id INT);"));

        var verified = CatalogVerifier.VerifySignature(json);

        Assert.Null(verified.ErrorCode);
        var root = JsonNode.Parse(json)!.AsObject();
        Assert.Equal(keyPair.PublicKeyBase64, root["publisher"]!["publicKey"]!["key"]!.GetValue<string>());
        Assert.Equal("ed25519", verified.Value!.Algorithm);
    }

    [Fact]
    public void PublishingADirectoryOfArtifactsProducesAVerifiableCatalog()
    {
        var keyPair = CatalogTestHelper.NewKeyPair();
        var directory = Path.Combine(Path.GetTempPath(), "forge-catalog-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var contents = new Dictionary<string, string>
        {
            ["customer-profile"] = "CREATE TABLE customer_profile (id INT);",
            ["profile-contacts"] = "CREATE TABLE profile_contact (id INT);",
            ["field-notes"] = "Customer Profiles content notes",
        };
        foreach (var (id, content) in contents)
        {
            File.WriteAllText(Path.Combine(directory, id + ".txt"), content);
        }

        // Id and version are supplied alongside the files, never parsed out of
        // the filenames (SPEC.md, Publishing).
        var inputs = contents
            .Select(pair => new ArtifactInput(pair.Key, "1.0.0", File.ReadAllBytes(Path.Combine(directory, pair.Key + ".txt")), "text/plain"))
            .ToArray();
        var json = CatalogTestHelper.Publish(keyPair.PrivateKeyBase64, inputs);
        var verified = CatalogVerifier.VerifySignature(json);
        Assert.Null(verified.ErrorCode);

        var artifacts = JsonNode.Parse(json)!["artifacts"]!.AsArray();
        Assert.Equal(contents.Count, artifacts.Count);
        foreach (var artifact in artifacts)
        {
            var id = artifact!["id"]!.GetValue<string>();
            var expected = System.Text.Encoding.UTF8.GetBytes(contents[id]);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), artifact["digest"]!["value"]!.GetValue<string>());
            Assert.Equal((long)expected.Length, artifact["size"]!.GetValue<long>());
        }
    }

    [Fact]
    public void NonSqlArtifactsPublishThroughTheSamePath()
    {
        var keyPair = CatalogTestHelper.NewKeyPair();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D };

        var json = CatalogTestHelper.Publish(
            keyPair.PrivateKeyBase64,
            new ArtifactInput("schema-diagram", "2.0.0", png, "image/png"),
            CatalogTestHelper.Artifact("field-notes", "1.0.0", "plain text, no SQL anywhere"));

        Assert.Null(CatalogVerifier.VerifySignature(json).ErrorCode);
        var artifacts = JsonNode.Parse(json)!["artifacts"]!.AsArray();
        Assert.Equal(2, artifacts.Count);
        Assert.Equal("sha256", artifacts[0]!["digest"]!["algorithm"]!.GetValue<string>());
        Assert.Equal(png.Length, artifacts[0]!["size"]!.GetValue<long>());
    }

    [Fact]
    public void GeneratedKeypairDerivesItsPublicHalfAndIsUnique()
    {
        var first = CatalogKeys.Generate();
        var second = CatalogKeys.Generate();

        Assert.Equal(first.PublicKeyBase64, CatalogKeys.PublicKeyOf(first.PrivateKeyBase64));
        Assert.NotEqual(first.PublicKeyBase64, second.PublicKeyBase64);
        Assert.NotEqual(first.PrivateKeyBase64, second.PrivateKeyBase64);
    }
}
