namespace Forge.Contracts;

// A validated package manifest (SQL.md, Manifest), restricted to the fields
// the PoC uses; any other field in manifest.yaml is rejected as unknown.
public sealed record PackageManifest(
    string Name,
    string Version,
    string Description,
    string TargetCore,
    string TargetDatabase);
