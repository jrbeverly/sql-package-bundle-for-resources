using System.Text.Json.Nodes;
using Forge.Contracts;

namespace Forge.Catalog;

// Maps the catalog document between records and JSON (SPEC.md, Catalog).
// The mapping is written out field by field rather than reflected, and only
// reads the fields this slice knows; unknown fields stay in the JSON tree,
// where the signature covers them (SPEC.md, Extensibility).
internal static class CatalogJson
{
    // Builds the document without the signature member; the signer
    // canonicalizes this and the signature is attached afterwards
    // (SPEC.md, Signing).
    public static JsonObject ToDocument(
        Publisher publisher,
        long version,
        DateTimeOffset created,
        DateTimeOffset? expires,
        IReadOnlyList<Artifact> artifacts,
        IReadOnlyList<CatalogReference> catalogs)
    {
        var document = new JsonObject
        {
            ["specVersion"] = 1,
            ["publisher"] = new JsonObject
            {
                ["id"] = publisher.Id,
                ["displayName"] = publisher.DisplayName,
                ["publicKey"] = new JsonObject
                {
                    ["type"] = publisher.PublicKey.Type,
                    ["key"] = publisher.PublicKey.Key,
                },
            },
            ["version"] = version,
            ["created"] = JsonTime.Format(created),
            ["artifacts"] = new JsonArray(artifacts.Select(ToDocument).ToArray()),
            ["catalogs"] = new JsonArray(catalogs.Select(ToDocument).ToArray()),
        };
        if (expires is { } expiry)
        {
            document["expires"] = JsonTime.Format(expiry);
        }

        return document;
    }

    private static JsonObject ToDocument(Artifact artifact)
    {
        var document = new JsonObject
        {
            ["id"] = artifact.Id,
            ["version"] = artifact.Version,
            ["digest"] = new JsonObject
            {
                ["algorithm"] = artifact.Digest.Algorithm,
                ["value"] = artifact.Digest.Value,
            },
            ["size"] = artifact.Size,
        };
        if (artifact.MediaType is not null)
        {
            document["mediaType"] = artifact.MediaType;
        }

        return document;
    }

    private static JsonObject ToDocument(CatalogReference reference) => new()
    {
        ["url"] = reference.Url,
        ["publisher"] = reference.Publisher,
        ["relationship"] = reference.Relationship,
    };

    // Reads the record out of a document whose signature already verified.
    // SPEC.md, Error Conditions has no code for a structurally unreadable
    // catalog, so E_CANONICALIZATION_FAILED stands in, naming the field.
    public static Result<Forge.Contracts.Catalog> Materialize(JsonObject root)
    {
        if (root["specVersion"] is not JsonValue specVersionValue || !specVersionValue.TryGetValue<int>(out var specVersion))
        {
            return Result<Forge.Contracts.Catalog>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog specVersion is not an integer.");
        }

        if (root["version"] is not JsonValue versionValue || !versionValue.TryGetValue<long>(out var version))
        {
            return Result<Forge.Contracts.Catalog>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog version is not an integer.");
        }

        if (!TryReadPublisher(root, out var publisher))
        {
            return Result<Forge.Contracts.Catalog>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog publisher block is unreadable.");
        }

        if (!TryReadTimestamp(root, "created", required: true, out var created))
        {
            return Result<Forge.Contracts.Catalog>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog created is not an RFC 3339 timestamp.");
        }

        if (!TryReadTimestamp(root, "expires", required: false, out var expires))
        {
            return Result<Forge.Contracts.Catalog>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog expires is not an RFC 3339 timestamp.");
        }

        if (root["artifacts"] is not JsonArray artifactsNode)
        {
            return Result<Forge.Contracts.Catalog>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog has no artifacts array.");
        }

        var artifacts = new List<Artifact>();
        for (var i = 0; i < artifactsNode.Count; i++)
        {
            if (artifactsNode[i] is not JsonObject artifactNode
                || artifactNode["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id)
                || artifactNode["version"] is not JsonValue artifactVersionValue || !artifactVersionValue.TryGetValue<string>(out var artifactVersion)
                || artifactNode["size"] is not JsonValue sizeValue || !sizeValue.TryGetValue<long>(out var size)
                || artifactNode["digest"] is not JsonObject digestNode
                || digestNode["algorithm"] is not JsonValue algorithmValue || !algorithmValue.TryGetValue<string>(out var algorithm)
                || digestNode["value"] is not JsonValue digestValue || !digestValue.TryGetValue<string>(out var digest))
            {
                return Result<Forge.Contracts.Catalog>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, $"catalog artifact {i} is missing id, version, size, or digest.");
            }

            artifacts.Add(new Artifact(id, artifactVersion, new Digest(algorithm, digest), size, ReadOptionalString(artifactNode, "mediaType")));
        }

        if (root["catalogs"] is not JsonArray catalogsNode)
        {
            return Result<Forge.Contracts.Catalog>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog has no catalogs array.");
        }

        var catalogs = new List<CatalogReference>();
        foreach (var referenceNode in catalogsNode)
        {
            if (referenceNode is not JsonObject reference || reference["url"] is not JsonValue urlValue || !urlValue.TryGetValue<string>(out var url))
            {
                return Result<Forge.Contracts.Catalog>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog reference is missing url.");
            }

            catalogs.Add(new CatalogReference(
                url,
                ReadOptionalString(reference, "publisher"),
                ReadOptionalString(reference, "relationship")));
        }

        if (root["signature"] is not JsonObject signatureNode
            || signatureNode["algorithm"] is not JsonValue signatureAlgorithmValue || !signatureAlgorithmValue.TryGetValue<string>(out var signatureAlgorithm)
            || signatureNode["value"] is not JsonValue signatureValue || !signatureValue.TryGetValue<string>(out var signature))
        {
            return Result<Forge.Contracts.Catalog>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog signature is unreadable.");
        }

        return Result<Forge.Contracts.Catalog>.Success(new Forge.Contracts.Catalog(
            specVersion,
            publisher,
            version,
            created,
            expires,
            artifacts,
            catalogs,
            new CatalogSignature(signatureAlgorithm, signature)));
    }

    private static bool TryReadPublisher(JsonObject root, out Publisher publisher)
    {
        publisher = null!;
        if (root["publisher"] is not JsonObject publisherNode
            || publisherNode["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id)
            || publisherNode["displayName"] is not JsonValue displayNameValue || !displayNameValue.TryGetValue<string>(out var displayName)
            || publisherNode["publicKey"] is not JsonObject publicKeyNode
            || publicKeyNode["type"] is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var type)
            || publicKeyNode["key"] is not JsonValue keyValue || !keyValue.TryGetValue<string>(out var key))
        {
            return false;
        }

        publisher = new Publisher(id, displayName, new PublisherKey(type, key));
        return true;
    }

    private static bool TryReadTimestamp(JsonObject root, string field, bool required, out DateTimeOffset value)
    {
        value = default;
        if (root[field] is not JsonValue fieldValue)
        {
            return !required;
        }

        return fieldValue.TryGetValue<string>(out var text) && JsonTime.TryParse(text, out value);
    }

    private static string? ReadOptionalString(JsonObject node, string field) =>
        node[field] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
