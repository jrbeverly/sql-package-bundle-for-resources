using System.IO.Compression;
using System.Security.Cryptography;
using Forge.Catalog;
using Forge.Contracts;

namespace Forge.Packaging;

// The distribution binding (SQL.md, Distribution Binding): resolve a package
// name in a verified, trusted catalog, verify the artifact bytes, extract
// them into a fresh directory, and check the package's own manifest identity
// against the catalog's claims. What comes out is a prepared package for the
// ordinary install path, indistinguishable from one read from a local
// directory. No database contact happens anywhere in here.
public static class CatalogPackageReader
{
    // SQL.md, Distribution Binding: the media type SQL packages travel under.
    public const string SqlPackageMediaType = "application/vnd.oac.sqlpackage.v1+zip";

    public static async Task<CatalogPackageResult> ReadAsync(
        string catalogJson,
        TrustStore trustStore,
        string packageName,
        byte[] artifactBytes,
        string configuredCore,
        CancellationToken cancellationToken = default)
    {
        // Verification — including the trust decision — runs before any byte
        // of the catalog is read as an artifact (SPEC.md, Discovery Flow).
        var verified = CatalogVerifier.Verify(catalogJson, trustStore);
        if (verified.ErrorCode is { } catalogError)
        {
            return CatalogPackageResult.Failure(catalogError, verified.Message!);
        }

        // Name resolution is restricted to artifacts of the SQL-package media
        // type; another media type sharing the name is not a candidate
        // (SQL.md, Distribution Binding, rule 5).
        var candidates = verified.Value!.Artifacts
            .Where(artifact => string.Equals(artifact.Id, packageName, StringComparison.Ordinal)
                && string.Equals(artifact.MediaType, SqlPackageMediaType, StringComparison.Ordinal))
            .ToList();
        if (candidates.Count != 1)
        {
            // SQL.md names no error code for a name that resolves to no single
            // artifact; the resolution-class code stands in (README Notes).
            return CatalogPackageResult.Failure(
                PackageErrorCode.E_DEPENDENCY_UNSATISFIED,
                candidates.Count == 0
                    ? $"no artifact named '{packageName}' with media type '{SqlPackageMediaType}' is offered by this catalog"
                    : $"this catalog offers {candidates.Count} artifacts named '{packageName}'; version-qualified selection is not implemented");
        }

        var artifact = candidates[0];
        var digestFailure = VerifyDigest(artifact, artifactBytes);
        if (digestFailure is not null)
        {
            // The bytes are discarded unextracted (rule 1).
            return CatalogPackageResult.Failure(CatalogErrorCode.E_DIGEST_MISMATCH, digestFailure);
        }

        var directory = Path.Combine(Path.GetTempPath(), $"forge_catalog_pkg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);

        var extractionFailure = Extract(artifactBytes, directory);
        if (extractionFailure is not null)
        {
            return Refuse(directory, PackageErrorCode.E_MANIFEST_INVALID, extractionFailure);
        }

        // Continue with the ordinary install path: an extracted package is a
        // package directory, nothing downstream behaves differently because it
        // was downloaded (rule 4).
        var prepared = await PackageReader.ReadAsync(directory, configuredCore, cancellationToken);
        if (prepared.ErrorCode is { } packageError)
        {
            return Refuse(directory, packageError, prepared.Message!);
        }

        // The catalog's claim about what it carries and the package's claim
        // about itself must agree (rule 3), naming both values.
        var manifest = prepared.Value!.Manifest;
        if (!string.Equals(manifest.Name, artifact.Id, StringComparison.Ordinal))
        {
            return Refuse(directory, PackageErrorCode.E_MANIFEST_INVALID,
                $"archive manifest name '{manifest.Name}' does not match the catalog artifact id '{artifact.Id}'");
        }

        if (!string.Equals(manifest.Version, artifact.Version, StringComparison.Ordinal))
        {
            return Refuse(directory, PackageErrorCode.E_MANIFEST_INVALID,
                $"archive manifest version '{manifest.Version}' does not match the catalog artifact version '{artifact.Version}'");
        }

        return CatalogPackageResult.Success(new CatalogPackage(prepared.Value, directory));
    }

    private static string? VerifyDigest(Artifact artifact, byte[] bytes)
    {
        if (!string.Equals(artifact.Digest.Algorithm, "sha256", StringComparison.Ordinal))
        {
            // SPEC.md, Artifact: a client MUST reject algorithms it does not
            // implement; SPEC.md names no separate code, so the digest-mismatch
            // code covers the refusal (README Notes).
            return $"artifact '{artifact.Id} {artifact.Version}' uses digest algorithm '{artifact.Digest.Algorithm}', which this client does not implement";
        }

        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        if (!string.Equals(actual, artifact.Digest.Value, StringComparison.Ordinal))
        {
            return $"artifact '{artifact.Id} {artifact.Version}' digest mismatch: the catalog records {artifact.Digest.Value}, the bytes hash to {actual}";
        }

        return null;
    }

    // Rule 2: reject any entry whose path is absolute, contains a .. segment,
    // or is a symbolic link, before writing it.
    private static string? Extract(byte[] archiveBytes, string directory)
    {
        using var stream = new MemoryStream(archiveBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            if (name.Length == 0 || name.EndsWith('/') || name.EndsWith('\\'))
            {
                continue;
            }

            if (Path.IsPathRooted(name) || name.Split(['/', '\\']).Any(segment => segment == ".."))
            {
                return $"archive entry '{name}' would escape the package directory";
            }

            if ((entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
            {
                return $"archive entry '{name}' is a symbolic link";
            }

            var destination = Path.Combine(directory, name);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var source = entry.Open();
            using var target = File.Create(destination);
            source.CopyTo(target);
        }

        return null;
    }

    // A refusal after the extraction directory exists must remove it: the
    // caller never learns the path of a failed extraction.
    private static CatalogPackageResult Refuse(string directory, PackageErrorCode errorCode, string message)
    {
        Directory.Delete(directory, recursive: true);
        return CatalogPackageResult.Failure(errorCode, message);
    }
}
