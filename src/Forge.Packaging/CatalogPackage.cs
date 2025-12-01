using Forge.Contracts;

namespace Forge.Packaging;

// A package extracted from a verified catalog artifact, prepared for the
// ordinary install path (SQL.md, Distribution Binding, rule 4). Directory is
// the fresh extraction directory the reader created; the caller installs from
// it and removes it when done.
public sealed record CatalogPackage(PreparedPackage Package, string Directory);
