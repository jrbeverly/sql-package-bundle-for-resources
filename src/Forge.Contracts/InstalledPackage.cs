namespace Forge.Contracts;

// One row of forge_installed_package (SQL.md, Installation Tracking).
public sealed record InstalledPackage(string Name, string Version, string State, DateTime InstalledAt);
