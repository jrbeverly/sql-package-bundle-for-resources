namespace Forge.Contracts;

// One entry of a realm bundle's spec.content.packages: a package the realm's
// world database is built from, resolved from the bundle's own packages/
// directory (DEPLOY.md, Environment Bundle).
public sealed record RealmPackageReference(string Name, string Version);
