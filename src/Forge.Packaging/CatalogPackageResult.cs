using Forge.Contracts;

namespace Forge.Packaging;

// The distribution binding's result: a prepared package, or a failure carrying
// whichever layer's error code the failing step belongs to. Catalog-layer
// failures keep their SPEC.md codes and package-layer failures their SQL.md
// codes, so a boundary maps each to its own exit class (TECHNICAL.md, Exit
// Codes).
public sealed record CatalogPackageResult(
    CatalogPackage? Value,
    PackageErrorCode? PackageError,
    CatalogErrorCode? CatalogError,
    string? Message)
{
    public static CatalogPackageResult Success(CatalogPackage value) => new(value, null, null, null);

    public static CatalogPackageResult Failure(PackageErrorCode errorCode, string message) =>
        new(null, errorCode, null, message);

    public static CatalogPackageResult Failure(CatalogErrorCode errorCode, string message) =>
        new(null, null, errorCode, message);
}
