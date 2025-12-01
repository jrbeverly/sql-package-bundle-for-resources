using System.Globalization;
using System.IO.Compression;
using Forge.Contracts;
using Forge.Database;
using Forge.Packaging;
using MySqlConnector;
using Xunit;

namespace Forge.Database.Tests;

// A package travelling from a trusted publisher's catalog into a primary
// database through the same install path as a local directory, and every
// refusal leaving the database untouched (issue acceptance criteria).
[Trait("Category", "Integration")]
public class CatalogInstallTests
{
    [Fact]
    public async Task CatalogInstallByNameMatchesALocalDirectoryInstall()
    {
        var zip = ZipDirectory(FixturePaths.ContentPackageV1);
        var keyPair = CatalogFixture.NewKeyPair();
        var catalog = CatalogFixture.Publish(keyPair.PrivateKeyBase64, CatalogFixture.Artifact("customer-profiles", "1.0.0", zip));
        var store = CatalogFixture.TrustedStore(keyPair.PrivateKeyBase64, catalog);

        await IntegrationMySql.InScratchDatabaseAsync(async catalogDatabase =>
            await IntegrationMySql.InScratchDatabaseAsync(async localDatabase =>
            {
                var resolved = await CatalogPackageReader.ReadAsync(catalog, store, "customer-profiles", zip, "example-app");
                Assert.Null(resolved.CatalogError);
                Assert.Null(resolved.PackageError);
                var catalogPackage = resolved.Value!;
                try
                {
                    var catalogInstall = await PackageInstaller.InstallAsync(
                        IntegrationMySql.ConnectionString, catalogDatabase, catalogPackage.Package, "integration-test");
                    Assert.Null(catalogInstall.ErrorCode);

                    var localPackage = await PackageReader.ReadAsync(FixturePaths.ContentPackageV1, "example-app");
                    Assert.Null(localPackage.ErrorCode);
                    var localInstall = await PackageInstaller.InstallAsync(
                        IntegrationMySql.ConnectionString, localDatabase, localPackage.Value!, "integration-test");
                    Assert.Null(localInstall.ErrorCode);

                    // Same tracking rows and same content. Timestamps and
                    // durations are install-run facts, not package facts, so
                    // the comparison excludes them.
                    foreach (var sql in new[]
                    {
                        "SELECT `name`, `version`, `target_core`, `state`, `installed_by` FROM `forge_installed_package`",
                        "SELECT `package_name`, `migration_file`, `package_version`, `migration_digest` FROM `forge_applied_migration` ORDER BY `migration_file`",
                        "SELECT * FROM `cp_customer_profile` ORDER BY `entry`",
                        "SELECT * FROM `cp_profile_contact` ORDER BY `entry`",
                    })
                    {
                        Assert.Equal(
                            JoinRows(await QueryRowsAsync(localDatabase, sql)),
                            JoinRows(await QueryRowsAsync(catalogDatabase, sql)));
                    }
                }
                finally
                {
                    Directory.Delete(catalogPackage.Directory, recursive: true);
                }
            }));
    }

    [Fact]
    public async Task ArtifactBytesWhoseDigestDoesNotMatchLeaveTheDatabaseUntouched()
    {
        var zip = ZipDirectory(FixturePaths.ContentPackageV1);
        var keyPair = CatalogFixture.NewKeyPair();
        var catalog = CatalogFixture.Publish(keyPair.PrivateKeyBase64, CatalogFixture.Artifact("customer-profiles", "1.0.0", zip));
        var store = CatalogFixture.TrustedStore(keyPair.PrivateKeyBase64, catalog);
        var tampered = zip.Concat([(byte)0x01]).ToArray();

        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var result = await CatalogPackageReader.ReadAsync(catalog, store, "customer-profiles", tampered, "example-app");

            Assert.Equal(CatalogErrorCode.E_DIGEST_MISMATCH, result.CatalogError);
            Assert.Null(result.Value);
            Assert.Empty(await QueryRowsAsync(database, TablesSql));
        });
    }

    [Fact]
    public async Task AnUntrustedPublisherIsRefusedBeforeAnySqlRuns()
    {
        var zip = ZipDirectory(FixturePaths.ContentPackageV1);
        var keyPair = CatalogFixture.NewKeyPair();
        var catalog = CatalogFixture.Publish(keyPair.PrivateKeyBase64, CatalogFixture.Artifact("customer-profiles", "1.0.0", zip));

        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var result = await CatalogPackageReader.ReadAsync(catalog, CatalogFixture.NewStore(), "customer-profiles", zip, "example-app");

            Assert.Equal(CatalogErrorCode.E_PUBLISHER_UNTRUSTED, result.CatalogError);
            Assert.Null(result.Value);
            Assert.Empty(await QueryRowsAsync(database, TablesSql));
        });
    }

    private const string TablesSql =
        "SELECT * FROM information_schema.tables WHERE table_schema = DATABASE() " +
        "AND table_name IN ('forge_installed_package', 'forge_applied_migration', 'cp_customer_profile', 'cp_profile_contact')";

    private static byte[] ZipDirectory(string directoryPath)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"forge_test_zip_{Guid.NewGuid():N}.zip");
        try
        {
            ZipFile.CreateFromDirectory(directoryPath, zipPath);
            return File.ReadAllBytes(zipPath);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    private static async Task<List<string[]>> QueryRowsAsync(string database, string sql)
    {
        await using var connection = new MySqlConnection(IntegrationMySql.ConnectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync(database);
        await using var command = new MySqlCommand(sql, connection);
        var rows = new List<string[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                values[i] = Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
            }

            rows.Add(values);
        }

        return rows;
    }

    private static List<string> JoinRows(List<string[]> rows) => rows.Select(r => string.Join("|", r)).ToList();
}
