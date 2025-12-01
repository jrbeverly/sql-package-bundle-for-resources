namespace Forge.Contracts;

// A signed catalog document (SPEC.md, Catalog).
public sealed record Catalog(
    int SpecVersion,
    Publisher Publisher,
    long Version,
    DateTimeOffset Created,
    DateTimeOffset? Expires,
    IReadOnlyList<Artifact> Artifacts,
    IReadOnlyList<CatalogReference> Catalogs,
    CatalogSignature Signature);
