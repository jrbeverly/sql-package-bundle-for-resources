using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Forge.Contracts;

namespace Forge.Catalog;

// The publisher side of SPEC.md, Publishing: each artifact becomes a record
// carrying its digest and byte length, the document is canonicalised and
// signed, and the output is verified through the client's own signature path
// before it is returned.
public static class CatalogPublisher
{
    public static Result<string> Publish(PublishRequest request)
    {
        string publicKeyBase64;
        try
        {
            publicKeyBase64 = CatalogKeys.PublicKeyOf(request.PrivateKeyBase64);
        }
        catch (FormatException)
        {
            return Result<string>.Failure(CatalogErrorCode.E_SIGNATURE_INVALID, "publisher private key is not valid base64.");
        }

        var artifacts = new List<Artifact>(request.Artifacts.Count);
        foreach (var input in request.Artifacts)
        {
            artifacts.Add(new Artifact(
                input.Id,
                input.Version,
                new Digest("sha256", Convert.ToHexString(SHA256.HashData(input.Content)).ToLowerInvariant()),
                input.Content.LongLength,
                input.MediaType));
        }

        var publisher = new Publisher(request.Id, request.DisplayName, new PublisherKey("ed25519", publicKeyBase64));
        var document = CatalogJson.ToDocument(publisher, request.Version, request.Created, request.Expires, artifacts, request.Catalogs);

        string json;
        try
        {
            var signingInput = Encoding.UTF8.GetBytes(Jcs.Canonicalize(document));
            var signature = CatalogSigner.Sign(signingInput, request.PrivateKeyBase64);
            document["signature"] = new JsonObject
            {
                ["algorithm"] = signature.Algorithm,
                ["value"] = signature.Value,
            };
            json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }
        catch (CanonicalizationException e)
        {
            return Result<string>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, e.Message);
        }

        // SPEC.md, Publishing: the publisher verifies its own output through
        // the same path a client uses, before writing it. Trust is not part of
        // that check — the author verifies the signature, the reader decides trust.
        var selfCheck = CatalogVerifier.VerifySignature(json);
        return selfCheck.ErrorCode is { } error
            ? Result<string>.Failure(error, $"published catalog failed its own signature verification: {selfCheck.Message}")
            : Result<string>.Success(json);
    }
}
