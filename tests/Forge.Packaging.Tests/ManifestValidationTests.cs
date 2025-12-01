using Forge.Contracts;
using Xunit;

namespace Forge.Packaging.Tests;

public class ManifestValidationTests
{
    private const string ValidManifest = """
        apiVersion: sqlpackage.v1
        kind: Package

        metadata:
          name: test-package
          version: 1.0.0
          description: A test package

        spec:
          targets:
            core: example-app
            database: primary
        """;

    [Fact]
    public async Task ValidManifestReadsEveryPoCField()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Null(result.ErrorCode);
        var manifest = result.Value!.Manifest;
        Assert.Equal("test-package", manifest.Name);
        Assert.Equal("1.0.0", manifest.Version);
        Assert.Equal("A test package", manifest.Description);
        Assert.Equal("example-app", manifest.TargetCore);
        Assert.Equal("primary", manifest.TargetDatabase);
    }

    [Fact]
    public async Task CommittedFixturePackagesPassValidation()
    {
        var fixtures = new[] { FixturePaths.ContentPackageV1, FixturePaths.ContentPackageV2, FixturePaths.IndependentPackage };
        foreach (var fixture in fixtures)
        {
            var result = await PackageReader.ReadAsync(fixture, "example-app");
            Assert.True(result.ErrorCode is null, $"{fixture}: {result.Message}");
        }
    }

    [Fact]
    public async Task MissingManifestIsRejected()
    {
        using var package = await TempPackage.CreateAsync(null, ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task NonexistentDirectoryIsRejected()
    {
        var result = await PackageReader.ReadAsync(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"forge_test_pkg_{Guid.NewGuid():N}"), "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task UnknownFieldIsRejected()
    {
        var manifest = ValidManifest.Replace("description: A test package", "description: A test package\n  author: Jon", StringComparison.Ordinal);
        using var package = await TempPackage.CreateAsync(manifest, ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
        Assert.Contains("author", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DependencyFieldIsRejectedAsNotYetSupported()
    {
        var manifest = """
            apiVersion: sqlpackage.v1
            kind: Package

            metadata:
              name: test-package
              version: 1.0.0
              description: A test package

            spec:
              targets:
                core: example-app
                database: primary
              dependencies:
                - name: base-schema
                  version: "*"
            """;
        using var package = await TempPackage.CreateAsync(manifest, ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
        Assert.Contains("dependencies", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongApiVersionIsRejected()
    {
        using var package = await TempPackage.CreateAsync(
            ValidManifest.Replace("sqlpackage.v1", "sqlpackage.v2", StringComparison.Ordinal), ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task WrongKindIsRejected()
    {
        using var package = await TempPackage.CreateAsync(
            ValidManifest.Replace("kind: Package", "kind: Bundle", StringComparison.Ordinal), ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task NameWithUppercaseIsRejected()
    {
        using var package = await TempPackage.CreateAsync(
            ValidManifest.Replace("name: test-package", "name: Test-Package", StringComparison.Ordinal), ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task NameStartingWithHyphenIsRejected()
    {
        using var package = await TempPackage.CreateAsync(
            ValidManifest.Replace("name: test-package", "name: -test-package", StringComparison.Ordinal), ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task VersionWithoutPatchIsRejected()
    {
        using var package = await TempPackage.CreateAsync(
            ValidManifest.Replace("version: 1.0.0", "version: 1.0", StringComparison.Ordinal), ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task VersionWithLeadingVIsRejected()
    {
        using var package = await TempPackage.CreateAsync(
            ValidManifest.Replace("version: 1.0.0", "version: v1.0.0", StringComparison.Ordinal), ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task MissingDescriptionIsRejected()
    {
        using var package = await TempPackage.CreateAsync(
            ValidManifest.Replace("  description: A test package\n", string.Empty, StringComparison.Ordinal), ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task MissingTargetCoreIsRejected()
    {
        using var package = await TempPackage.CreateAsync(
            ValidManifest.Replace("    core: example-app\n", string.Empty, StringComparison.Ordinal), ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task UnknownTargetDatabaseIsRejected()
    {
        using var package = await TempPackage.CreateAsync(
            ValidManifest.Replace("database: primary", "database: logs", StringComparison.Ordinal), ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task SyntacticallyInvalidManifestIsRejected()
    {
        using var package = await TempPackage.CreateAsync("apiVersion: [unclosed", ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }
}
