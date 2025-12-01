using Forge.Contracts;
using Forge.Packaging;

namespace Forge.Environments;

// Resolves a bundle's package references against the bundle's own packages/
// directory into a full install plan (DEPLOY.md, Environment Bundle; POC.md
// keeps resolution local to the bundle, so no catalog is consulted). An
// unresolvable reference stops the deployment before anything is written.
public static class RealmPackageResolver
{
    public static async Task<RealmResult<IReadOnlyList<PreparedPackage>>> ResolveAsync(
        string bundlePath,
        RealmBundle bundle,
        string configuredCore,
        CancellationToken cancellationToken = default)
    {
        var prepared = new List<PreparedPackage>(bundle.Packages.Count);
        foreach (var reference in bundle.Packages)
        {
            var packagePath = Path.Combine(bundlePath, "packages", reference.Name);
            var read = await PackageReader.ReadAsync(packagePath, configuredCore, cancellationToken);
            if (read.ErrorCode is not null)
            {
                // The package layer's own code names the failure; the realm
                // layer reports it as an unresolvable bundle reference.
                return RealmResult<IReadOnlyList<PreparedPackage>>.Failure(
                    RealmErrorCode.E_PACKAGE_UNRESOLVED,
                    $"bundle references package '{reference.Name} {reference.Version}' but it cannot be resolved: " +
                    $"{read.ErrorCode.Value}: {read.Message}");
            }

            var package = read.Value!;
            if (!string.Equals(package.Manifest.Version, reference.Version, StringComparison.Ordinal))
            {
                return RealmResult<IReadOnlyList<PreparedPackage>>.Failure(
                    RealmErrorCode.E_PACKAGE_UNRESOLVED,
                    $"bundle references package '{reference.Name} {reference.Version}' but the bundle's package is version {package.Manifest.Version}");
            }

            prepared.Add(package);
        }

        return RealmResult<IReadOnlyList<PreparedPackage>>.Success(prepared);
    }
}
