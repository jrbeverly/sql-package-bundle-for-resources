using System.Text.Json;
using System.Text.Json.Nodes;
using Forge.Contracts;

namespace Forge.Catalog;

// Reads the publisher identity block out of a catalog document. Identity is
// what an operator must be shown before trusting a publisher; artifacts are
// what must not be read from an unverified catalog, and this reads none of
// them (SPEC.md, Trust Model).
public static class CatalogReader
{
    public static Result<Publisher> ReadPublisher(string catalogJson)
    {
        if (!TryParseRoot(catalogJson, out var root))
        {
            return Result<Publisher>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog is not a valid JSON document.");
        }

        if (root["publisher"] is not JsonObject publisherNode
            || publisherNode["id"] is not JsonValue idValue || !idValue.TryGetValue<string>(out var id)
            || publisherNode["displayName"] is not JsonValue displayNameValue || !displayNameValue.TryGetValue<string>(out var displayName)
            || publisherNode["publicKey"] is not JsonObject publicKeyNode
            || publicKeyNode["type"] is not JsonValue typeValue || !typeValue.TryGetValue<string>(out var type)
            || publicKeyNode["key"] is not JsonValue keyValue || !keyValue.TryGetValue<string>(out var key))
        {
            return Result<Publisher>.Failure(CatalogErrorCode.E_CANONICALIZATION_FAILED, "catalog publisher block is unreadable.");
        }

        return Result<Publisher>.Success(new Publisher(id, displayName, new PublisherKey(type, key)));
    }

    internal static bool TryParseRoot(string catalogJson, out JsonObject root)
    {
        root = null!;
        try
        {
            if (JsonNode.Parse(catalogJson) is not JsonObject parsed)
            {
                return false;
            }

            root = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
