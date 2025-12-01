namespace Forge.Contracts;

// The signature member of a catalog (SPEC.md, Signing).
public sealed record CatalogSignature(string Algorithm, string Value);
