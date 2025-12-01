namespace Forge.Contracts;

// The outcome of an upgrade (SQL.md, Operations, Upgrade): the package row
// afterwards, and whether the recorded version changed — an upgrade to the
// version already installed is a no-op, not an error.
public sealed record PackageUpgrade(InstalledPackage Package, bool VersionChanged);
