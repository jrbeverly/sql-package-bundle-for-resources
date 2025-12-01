using System.Text;
using System.Text.Json.Nodes;
using Forge.Contracts;

namespace Forge.Catalog;

// The client-side verification pipeline, in the order HLD.md fixes:
// spec version, canonicalisation, signature, trust. Verification fails
// closed: artifacts are materialized only into the success value, so nothing
// in an unverified catalog is read as artifacts (SPEC.md, Discovery Flow).
// Expiry and rollback checks arrive with their own work items.
public static class CatalogVerifier
{
    public static Result<Forge.Contracts.Catalog> Verify(string catalogJson, TrustStore trustStore)
    {
        if (!CatalogReader.TryParseRoot(catalogJson, out var root))
        {
            return Result<Forge.Contracts.Catalog>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog is not a valid JSON document.");
        }

        var signatureResult = VerifySignatureOf(root);
        if (signatureResult.ErrorCode is { } error)
        {
            return Result<Forge.Contracts.Catalog>.Failure(error, signatureResult.Message!);
        }

        var publisherKey = root["publisher"]!["publicKey"]!["key"]!.GetValue<string>();
        if (!trustStore.IsTrusted(publisherKey))
        {
            return Result<Forge.Contracts.Catalog>.Failure(
                CatalogErrorCode.E_PUBLISHER_UNTRUSTED,
                $"Publisher '{PublisherDisplayNameOf(root)}' with key {publisherKey} is not trusted; enrol the publisher explicitly before reading this catalog.");
        }

        return CatalogJson.Materialize(root);
    }

    // The signature half of the pipeline, shared by Verify above and by the
    // publisher's self-check (SPEC.md, Publishing: a publisher verifies its
    // own output through the same path a client uses). Trust is not part of
    // it: the author verifies the signature, the reader decides trust.
    public static Result<CatalogSignature> VerifySignature(string catalogJson)
    {
        if (!CatalogReader.TryParseRoot(catalogJson, out var root))
        {
            return Result<CatalogSignature>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog is not a valid JSON document.");
        }

        return VerifySignatureOf(root);
    }

    private static Result<CatalogSignature> VerifySignatureOf(JsonObject root)
    {
        if (root["specVersion"] is not JsonValue specVersionValue || !specVersionValue.TryGetValue<int>(out var specVersion) || specVersion != 1)
        {
            var shown = root["specVersion"]?.ToJsonString() ?? "missing";
            return Result<CatalogSignature>.Failure(
                CatalogErrorCode.E_SPEC_VERSION_UNSUPPORTED,
                $"catalog specVersion {shown} is not supported; this client implements 1.");
        }

        if (root["signature"] is not JsonObject signatureNode
            || signatureNode["algorithm"] is not JsonValue algorithmValue || !algorithmValue.TryGetValue<string>(out var algorithm)
            || signatureNode["value"] is not JsonValue signatureTextValue || !signatureTextValue.TryGetValue<string>(out var signatureText))
        {
            return Result<CatalogSignature>.Failure(CatalogErrorCode.E_SIGNATURE_INVALID, "catalog carries no readable signature.");
        }

        if (root["publisher"] is not JsonObject publisherNode
            || publisherNode["publicKey"] is not JsonObject publicKeyNode
            || publicKeyNode["type"] is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var keyType)
            || publicKeyNode["key"] is not JsonValue keyValue || !keyValue.TryGetValue<string>(out var publisherKey))
        {
            return Result<CatalogSignature>.Failure(CatalogErrorCode.E_SIGNATURE_INVALID, "catalog publisher carries no readable public key.");
        }

        if (algorithm != keyType)
        {
            return Result<CatalogSignature>.Failure(
                CatalogErrorCode.E_SIGNATURE_INVALID,
                $"catalog signature algorithm '{algorithm}' does not match publisher key type '{keyType}'.");
        }

        byte[] signingInput;
        try
        {
            var withoutSignature = (JsonObject)root.DeepClone();
            withoutSignature.Remove("signature");
            signingInput = Encoding.UTF8.GetBytes(Jcs.Canonicalize(withoutSignature));
        }
        catch (CanonicalizationException e)
        {
            return Result<CatalogSignature>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, e.Message);
        }

        var signature = new CatalogSignature(algorithm, signatureText);
        bool valid;
        try
        {
            valid = CatalogSigner.Verify(signingInput, signature, publisherKey);
        }
        catch (FormatException)
        {
            return Result<CatalogSignature>.Failure(CatalogErrorCode.E_SIGNATURE_INVALID, "catalog signature or publisher key is not valid base64.");
        }

        if (!valid)
        {
            return Result<CatalogSignature>.Failure(
                CatalogErrorCode.E_SIGNATURE_INVALID,
                $"catalog signature does not verify against its own publisher key {publisherKey}.");
        }

        return Result<CatalogSignature>.Success(signature);
    }

    private static string PublisherDisplayNameOf(JsonObject root) =>
        root["publisher"] is JsonObject publisherNode
        && publisherNode["displayName"] is JsonValue displayNameValue
        && displayNameValue.TryGetValue<string>(out var displayName)
            ? displayName
            : "unknown";
}
