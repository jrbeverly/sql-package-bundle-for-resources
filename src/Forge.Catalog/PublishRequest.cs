using Forge.Contracts;

namespace Forge.Catalog;

// What publishing needs: the publisher's identity labels, the private key,
// and the catalog's version. The caller supplies the version — one greater
// than the previously published catalog's, or 1 for a first catalog
// (SPEC.md, Publishing); the slice never guesses it.
public sealed record PublishRequest(
    string Id,
    string DisplayName,
    string PrivateKeyBase64,
    long Version,
    DateTimeOffset Created,
    DateTimeOffset? Expires,
    IReadOnlyList<ArtifactInput> Artifacts,
    IReadOnlyList<CatalogReference> Catalogs);
