namespace Forge.Contracts;

// A recommendation of another catalog (SPEC.md, Catalog Discovery). `Publisher`
// is a label and proves nothing.
public sealed record CatalogReference(string Url, string? Publisher, string? Relationship);
