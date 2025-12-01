using System.IO.Compression;
using Forge.Contracts;
using Xunit;

namespace Forge.Packaging.Tests;

// The distribution binding rules (SQL.md, Distribution Binding) with no
// database: name resolution, digest verification, extraction safety, and the
// manifest-versus-catalog identity check.
public class CatalogPackageReaderTests
{
    private const string ConfiguredCore = "example-app";

    private const string ValidManifest = """
        apiVersion: sqlpackage.v1
        kind: Package

        metadata:
          name: customer-profiles
          version: 1.0.0
          description: A test package

        spec:
          targets:
            core: example-app
            database: primary
        """;

    private const string MismatchedNameManifest = """
        apiVersion: sqlpackage.v1
        kind: Package

        metadata:
          name: real-name
          version: 1.0.0
          description: A test package

        spec:
          targets:
            core: example-app
            database: primary
        """;

    [Fact]
    public async Task ATrustedCatalogArtifactResolvesByNameIntoAPreparedPackage()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_initial.sql", "SELECT 1;"));
        var zip = ZipPackage(package.Path);
        var keyPair = CatalogFixture.NewKeyPair();
        var catalog = CatalogFixture.Publish(keyPair.PrivateKeyBase64, CatalogFixture.Artifact("customer-profiles", "1.0.0", zip));
        var store = CatalogFixture.TrustedStore(keyPair.PrivateKeyBase64, catalog);

        var result = await CatalogPackageReader.ReadAsync(catalog, store, "customer-profiles", zip, ConfiguredCore);

        Assert.Null(result.PackageError);
        Assert.Null(result.CatalogError);
        var resolved = result.Value!;
        Assert.Equal("customer-profiles", resolved.Package.Manifest.Name);
        Assert.Equal("1.0.0", resolved.Package.Manifest.Version);
        Assert.Equal("001_initial.sql", Assert.Single(resolved.Package.Migrations).FileName);

        // The extracted directory is a package directory: manifest at the
        // root, migrations under migrations/.
        Assert.True(File.Exists(Path.Combine(resolved.Directory, "manifest.yaml")));
        Assert.True(File.Exists(Path.Combine(resolved.Directory, "migrations", "001_initial.sql")));
        Directory.Delete(resolved.Directory, recursive: true);
    }

    [Fact]
    public async Task ArtifactBytesWhoseDigestDoesNotMatchAreRefused()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_initial.sql", "SELECT 1;"));
        var zip = ZipPackage(package.Path);
        var keyPair = CatalogFixture.NewKeyPair();
        var catalog = CatalogFixture.Publish(keyPair.PrivateKeyBase64, CatalogFixture.Artifact("customer-profiles", "1.0.0", zip));
        var store = CatalogFixture.TrustedStore(keyPair.PrivateKeyBase64, catalog);
        var tampered = zip.Concat([(byte)0x01]).ToArray();

        var result = await CatalogPackageReader.ReadAsync(catalog, store, "customer-profiles", tampered, ConfiguredCore);

        Assert.Equal(CatalogErrorCode.E_DIGEST_MISMATCH, result.CatalogError);
        Assert.Null(result.PackageError);
        Assert.Null(result.Value);
        Assert.Contains("customer-profiles 1.0.0", result.Message, StringComparison.Ordinal);
        Assert.Contains("digest mismatch", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUntrustedPublisherIsRefusedBeforeAnythingIsRead()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_initial.sql", "SELECT 1;"));
        var zip = ZipPackage(package.Path);
        var keyPair = CatalogFixture.NewKeyPair();
        var catalog = CatalogFixture.Publish(keyPair.PrivateKeyBase64, CatalogFixture.Artifact("customer-profiles", "1.0.0", zip));

        var result = await CatalogPackageReader.ReadAsync(catalog, CatalogFixture.NewStore(), "customer-profiles", zip, ConfiguredCore);

        Assert.Equal(CatalogErrorCode.E_PUBLISHER_UNTRUSTED, result.CatalogError);
        Assert.Null(result.PackageError);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ManifestNameDisagreementWithTheCatalogEntryNamesBothValues()
    {
        using var package = await TempPackage.CreateAsync(MismatchedNameManifest, ("001_initial.sql", "SELECT 1;"));
        var zip = ZipPackage(package.Path);
        var keyPair = CatalogFixture.NewKeyPair();
        var catalog = CatalogFixture.Publish(keyPair.PrivateKeyBase64, CatalogFixture.Artifact("catalog-name", "1.0.0", zip));
        var store = CatalogFixture.TrustedStore(keyPair.PrivateKeyBase64, catalog);

        var result = await CatalogPackageReader.ReadAsync(catalog, store, "catalog-name", zip, ConfiguredCore);

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.PackageError);
        Assert.Null(result.CatalogError);
        Assert.Null(result.Value);
        Assert.Contains("real-name", result.Message, StringComparison.Ordinal);
        Assert.Contains("catalog-name", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManifestVersionDisagreementWithTheCatalogEntryNamesBothValues()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_initial.sql", "SELECT 1;"));
        var zip = ZipPackage(package.Path);
        var keyPair = CatalogFixture.NewKeyPair();
        var catalog = CatalogFixture.Publish(keyPair.PrivateKeyBase64, CatalogFixture.Artifact("customer-profiles", "2.0.0", zip));
        var store = CatalogFixture.TrustedStore(keyPair.PrivateKeyBase64, catalog);

        var result = await CatalogPackageReader.ReadAsync(catalog, store, "customer-profiles", zip, ConfiguredCore);

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.PackageError);
        Assert.Null(result.CatalogError);
        Assert.Null(result.Value);
        Assert.Contains("1.0.0", result.Message, StringComparison.Ordinal);
        Assert.Contains("2.0.0", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnArtifactOfAnotherMediaTypeIsNotAResolutionCandidate()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_initial.sql", "SELECT 1;"));
        var zip = ZipPackage(package.Path);
        var keyPair = CatalogFixture.NewKeyPair();
        var catalog = CatalogFixture.Publish(keyPair.PrivateKeyBase64, CatalogFixture.Artifact("customer-profiles", "1.0.0", zip, mediaType: "text/plain"));
        var store = CatalogFixture.TrustedStore(keyPair.PrivateKeyBase64, catalog);

        var result = await CatalogPackageReader.ReadAsync(catalog, store, "customer-profiles", zip, ConfiguredCore);

        Assert.Equal(PackageErrorCode.E_DEPENDENCY_UNSATISFIED, result.PackageError);
        Assert.Null(result.CatalogError);
        Assert.Null(result.Value);
        Assert.Contains("customer-profiles", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEntryWithAnAbsolutePathIsRefused()
    {
        var result = await ResolveHostileZipAsync(ZipWithEntry("/outside.sql"));

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.PackageError);
        Assert.Contains("/outside.sql", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEntryWithParentTraversalIsRefused()
    {
        var result = await ResolveHostileZipAsync(ZipWithEntry("../outside.sql"));

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.PackageError);
        Assert.Contains("../outside.sql", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASymlinkEntryIsRefused()
    {
        var result = await ResolveHostileZipAsync(ZipWithEntry("outside.sql", symlink: true));

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.PackageError);
        Assert.Contains("outside.sql", result.Message, StringComparison.Ordinal);
    }

    private static async Task<CatalogPackageResult> ResolveHostileZipAsync(byte[] zip)
    {
        var keyPair = CatalogFixture.NewKeyPair();
        var catalog = CatalogFixture.Publish(keyPair.PrivateKeyBase64, CatalogFixture.Artifact("customer-profiles", "1.0.0", zip));
        var store = CatalogFixture.TrustedStore(keyPair.PrivateKeyBase64, catalog);
        return await CatalogPackageReader.ReadAsync(catalog, store, "customer-profiles", zip, ConfiguredCore);
    }

    private static byte[] ZipPackage(string packagePath)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"forge_test_zip_{Guid.NewGuid():N}.zip");
        try
        {
            ZipFile.CreateFromDirectory(packagePath, zipPath);
            return File.ReadAllBytes(zipPath);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    private static byte[] ZipWithEntry(string entryName, bool symlink = false)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(entryName);
            if (symlink)
            {
                entry.ExternalAttributes = 0xA000 << 16;
            }

            using var writer = new StreamWriter(entry.Open());
            writer.Write("content");
        }

        return stream.ToArray();
    }
}
