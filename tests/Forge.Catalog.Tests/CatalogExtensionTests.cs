using System.Text;
using System.Text.Json.Nodes;
using Forge.Contracts;
using Xunit;

namespace Forge.Catalog.Tests;

// Unknown fields are covered by the signature (SPEC.md, Extensibility), and
// the pipeline refuses an unsupported specVersion before anything else.
public class CatalogExtensionTests
{
    [Fact]
    public void ACatalogCarryingUnknownFieldsVerifiesAndTamperingThemFails()
    {
        var keyPair = CatalogTestHelper.NewKeyPair();
        var json = CatalogTestHelper.Publish(keyPair.PrivateKeyBase64, CatalogTestHelper.Artifact("customer-profile", "1.0.0", "CREATE TABLE customer_profile (id INT);"));
        var withExtension = Resign(json, keyPair.PrivateKeyBase64, node => node["x-extension"] = new JsonObject { ["note"] = "carried for later" });
        var store = CatalogTestHelper.TrustedStore(keyPair.PrivateKeyBase64, withExtension);

        Assert.Null(CatalogVerifier.Verify(withExtension, store).ErrorCode);

        var tampered = withExtension.Replace("carried for later", "tampered", StringComparison.Ordinal);
        var result = CatalogVerifier.Verify(tampered, store);

        Assert.Equal(CatalogErrorCode.E_SIGNATURE_INVALID, result.ErrorCode);
        Assert.Null(result.Value);
    }

    [Fact]
    public void AnUnsupportedSpecVersionIsRefusedBeforeSignatureAndTrust()
    {
        var keyPair = CatalogTestHelper.NewKeyPair();
        var json = CatalogTestHelper.Publish(keyPair.PrivateKeyBase64, CatalogTestHelper.Artifact("customer-profile", "1.0.0", "CREATE TABLE customer_profile (id INT);"));
        // Re-signed, so the signature is valid for the new content: only the
        // specVersion check can refuse it.
        var future = Resign(json, keyPair.PrivateKeyBase64, node => node["specVersion"] = 2);
        var store = CatalogTestHelper.TrustedStore(keyPair.PrivateKeyBase64, json);

        var result = CatalogVerifier.Verify(future, store);

        Assert.Equal(CatalogErrorCode.E_SPEC_VERSION_UNSUPPORTED, result.ErrorCode);
        Assert.Null(result.Value);
    }

    // Re-signs a modified document through the public signing path: strip the
    // signature, canonicalise, sign, reinsert.
    private static string Resign(string json, string privateKeyBase64, Action<JsonObject> change)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        change(root);
        root.Remove("signature");
        var signingInput = Encoding.UTF8.GetBytes(Jcs.Canonicalize(root));
        var signature = CatalogSigner.Sign(signingInput, privateKeyBase64);
        root["signature"] = new JsonObject
        {
            ["algorithm"] = signature.Algorithm,
            ["value"] = signature.Value,
        };
        return root.ToJsonString();
    }
}
