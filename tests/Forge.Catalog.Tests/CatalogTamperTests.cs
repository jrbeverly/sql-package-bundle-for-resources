using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Forge.Contracts;
using Xunit;

namespace Forge.Catalog.Tests;

// Acceptance: changing one byte of a published catalog, or of its signature,
// makes verification fail and nothing is read from it.
public class CatalogTamperTests
{
    [Fact]
    public void ChangingOneByteOfAPublishedCatalogFailsVerificationAndReadsNothing()
    {
        var keyPair = CatalogTestHelper.NewKeyPair();
        const string content = "CREATE TABLE customer_profile (id INT);";
        var json = CatalogTestHelper.Publish(keyPair.PrivateKeyBase64, CatalogTestHelper.Artifact("customer-profile", "1.0.0", content));
        var store = CatalogTestHelper.TrustedStore(keyPair.PrivateKeyBase64, json);

        // Flip one hex digit of the artifact digest: still valid JSON, but a
        // different document than the one that was signed.
        var digest = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
        var tampered = json.Replace(digest, digest[..^1] + (digest[^1] == '0' ? '1' : '0'), StringComparison.Ordinal);
        Assert.NotEqual(json, tampered);

        var result = CatalogVerifier.Verify(tampered, store);

        Assert.Equal(CatalogErrorCode.E_SIGNATURE_INVALID, result.ErrorCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public void ChangingOneByteOfTheSignatureFailsVerificationAndReadsNothing()
    {
        var keyPair = CatalogTestHelper.NewKeyPair();
        var json = CatalogTestHelper.Publish(keyPair.PrivateKeyBase64, CatalogTestHelper.Artifact("customer-profile", "1.0.0", "CREATE TABLE customer_profile (id INT);"));
        var store = CatalogTestHelper.TrustedStore(keyPair.PrivateKeyBase64, json);

        var root = JsonNode.Parse(json)!.AsObject();
        var signatureValue = root["signature"]!["value"]!.GetValue<string>();
        var flipped = signatureValue[..^1] + (signatureValue[^1] == 'A' ? 'B' : 'A');
        root["signature"]!["value"] = flipped;
        var tampered = root.ToJsonString();
        Assert.NotEqual(json, tampered);

        var result = CatalogVerifier.Verify(tampered, store);

        Assert.Equal(CatalogErrorCode.E_SIGNATURE_INVALID, result.ErrorCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public void AValidButDifferentlySignedCatalogIsRefused()
    {
        var attacker = CatalogTestHelper.NewKeyPair();
        var json = CatalogTestHelper.Publish(attacker.PrivateKeyBase64, CatalogTestHelper.Artifact("customer-profile", "1.0.0", "DROP TABLE customer_profile;"));
        var victim = CatalogTestHelper.NewKeyPair();
        var store = CatalogTestHelper.TrustedStore(victim.PrivateKeyBase64, CatalogTestHelper.Publish(victim.PrivateKeyBase64, CatalogTestHelper.Artifact("customer-profile", "1.0.0", "CREATE TABLE customer_profile (id INT);")));

        // Well signed, but not by the trusted publisher's key.
        var result = CatalogVerifier.Verify(json, store);

        Assert.Equal(CatalogErrorCode.E_PUBLISHER_UNTRUSTED, result.ErrorCode);
        Assert.Null(result.Value);
    }
}
