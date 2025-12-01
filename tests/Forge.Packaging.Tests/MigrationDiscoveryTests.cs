using Forge.Contracts;
using Xunit;

namespace Forge.Packaging.Tests;

public class MigrationDiscoveryTests
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
    public async Task MigrationsAreOrderedNumerically()
    {
        using var package = await TempPackage.CreateAsync(
            ValidManifest,
            ("010_tenth.sql", "SELECT 10;"),
            ("002_second.sql", "SELECT 2;"),
            ("009_ninth.sql", "SELECT 9;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Null(result.ErrorCode);
        Assert.Equal([2, 9, 10], result.Value!.Migrations.Select(m => m.Number));
    }

    [Fact]
    public async Task NumberingGapsAreAllowed()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_first.sql", "SELECT 1;"), ("004_fourth.sql", "SELECT 4;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Null(result.ErrorCode);
        Assert.Equal([1, 4], result.Value!.Migrations.Select(m => m.Number));
    }

    [Fact]
    public async Task DuplicatePrefixIsRejected()
    {
        using var package = await TempPackage.CreateAsync(
            ValidManifest, ("001_alpha.sql", "SELECT 1;"), ("001_beta.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MIGRATION_DUPLICATE_PREFIX, result.ErrorCode);
        Assert.Contains("001_alpha.sql", result.Message, StringComparison.Ordinal);
        Assert.Contains("001_beta.sql", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FileWithoutNumericPrefixIsRejected()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MIGRATION_NAME_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task TwoDigitPrefixIsRejected()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("01_short.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MIGRATION_NAME_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task UppercaseSlugIsRejected()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_Initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MIGRATION_NAME_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task WrongExtensionIsRejected()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_initial.txt", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MIGRATION_NAME_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task UppercaseExtensionIsRejected()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_initial.SQL", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MIGRATION_NAME_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task MissingMigrationsDirectoryIsRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge_test_pkg_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(path);
            await File.WriteAllTextAsync(Path.Combine(path, "manifest.yaml"), ValidManifest);

            var result = await PackageReader.ReadAsync(path, "example-app");

            Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
        }
        finally
        {
            Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public async Task EmptyMigrationsDirectoryIsRejected()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest);

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Equal(PackageErrorCode.E_MANIFEST_INVALID, result.ErrorCode);
    }
}
