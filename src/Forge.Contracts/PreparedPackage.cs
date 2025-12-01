namespace Forge.Contracts;

// A package that passed manifest validation, migration discovery, and target
// matching: the plan Forge.Packaging builds and Forge.Database executes
// (HLD.md, Major Components).
public sealed record PreparedPackage(PackageManifest Manifest, IReadOnlyList<MigrationFile> Migrations);
