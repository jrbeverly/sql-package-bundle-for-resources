using System.Globalization;
using System.Security.Cryptography;
using Forge.Contracts;
using Forge.Database;
using MySqlConnector;
using Xunit;

namespace Forge.Database.Tests;

[Trait("Category", "Integration")]
public class PackageInstallTrackingTests
{
    [Fact]
    public async Task InstallRecordsThePackageAndEveryMigrationInTheTrackingTables()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var install = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1(), "integration-test");

            Assert.Null(install.ErrorCode);
            Assert.Equal("customer-profiles", install.Value!.Name);
            Assert.Equal("1.0.0", install.Value.Version);
            Assert.Equal("installed", install.Value.State);

            // The same facts, read back from the database directly.
            var installedRow = Assert.Single(await QueryRowsAsync(
                database, "SELECT `name`, `version`, `target_core`, `state`, `installed_by` FROM `forge_installed_package`"));
            Assert.Equal(["customer-profiles", "1.0.0", "example-app", "installed", "integration-test"], installedRow);

            var migrationRows = await QueryRowsAsync(
                database, "SELECT `migration_file`, `package_version`, `duration_ms` FROM `forge_applied_migration` ORDER BY `migration_file`");
            Assert.Equal(2, migrationRows.Count);
            Assert.Equal("001_customer_profiles.sql", migrationRows[0][0]);
            Assert.Equal("002_profile_contacts.sql", migrationRows[1][0]);
            foreach (var row in migrationRows)
            {
                Assert.Equal("1.0.0", row[1]);
                Assert.True(int.Parse(row[2], CultureInfo.InvariantCulture) >= 0, $"duration_ms was {row[2]}");
            }

            // Each recorded digest is the SHA-256 of the file bytes.
            var digestRows = await QueryRowsAsync(
                database, "SELECT `migration_file`, `migration_digest` FROM `forge_applied_migration` ORDER BY `migration_file`");
            foreach (var row in digestRows)
            {
                var bytes = await File.ReadAllBytesAsync(Path.Combine(FixturePaths.ContentPackageV1, "migrations", row[0]));
                Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), row[1]);
            }

            // The fixture content: user variables set in one statement and
            // used in later ones of the same batch, ending in an UPDATE.
            Assert.Equal(
                ["400001|Northwind Supplies|1", "400002|Southridge Services|2"],
                JoinRows(await QueryRowsAsync(database, "SELECT `entry`, `name`, `status` FROM `cp_customer_profile` ORDER BY `entry`")));
            Assert.Equal(
                ["400101|Operations Contact|400001", "400102|Billing Contact|400002"],
                JoinRows(await QueryRowsAsync(database, "SELECT `entry`, `name`, `profile_entry` FROM `cp_profile_contact` ORDER BY `entry`")));
        });
    }

    [Fact]
    public async Task InstalledContentMatchesHandAppliedMigrations()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async installedDatabase =>
        {
            var install = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, installedDatabase, CustomerProfilesV1(), "integration-test");
            Assert.Null(install.ErrorCode);

            var handDatabase = $"{installedDatabase}_hand";
            try
            {
                await IntegrationMySql.ExecuteAdminAsync($"CREATE DATABASE `{handDatabase}`");

                // What the mysql client does with each file: send it as one
                // multi-statement batch. The client binary is not installed on
                // the harness, so the same bytes go through MySqlConnector.
                var files = Directory.GetFiles(Path.Combine(FixturePaths.ContentPackageV1, "migrations"))
                    .OrderBy(f => f, StringComparer.Ordinal);
                foreach (var file in files)
                {
                    await MigrationExecutor.ExecuteBatchAsync(
                        IntegrationMySql.ConnectionString, handDatabase, await File.ReadAllTextAsync(file));
                }

                foreach (var table in new[] { "cp_customer_profile", "cp_profile_contact" })
                {
                    var installed = JoinRows(await QueryRowsAsync(installedDatabase, $"SELECT * FROM `{table}` ORDER BY 1"));
                    var hand = JoinRows(await QueryRowsAsync(handDatabase, $"SELECT * FROM `{table}` ORDER BY 1"));
                    Assert.Equal(hand, installed);
                }
            }
            finally
            {
                await IntegrationMySql.ExecuteAdminAsync($"DROP DATABASE IF EXISTS `{handDatabase}`");
            }
        });
    }

    [Fact]
    public async Task ReinstallingTheSamePackageIsRefusedWithoutReapplyingAnything()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var first = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1(), "integration-test");
            Assert.Null(first.ErrorCode);

            var appliedAtBefore = JoinRows(await QueryRowsAsync(
                database, "SELECT `applied_at` FROM `forge_applied_migration` ORDER BY `migration_file`"));

            var second = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1(), "integration-test");

            Assert.Equal(PackageErrorCode.E_ALREADY_INSTALLED, second.ErrorCode);
            Assert.Contains("1.0.0", second.Message, StringComparison.Ordinal);

            // Nothing ran: the recorded applied_at timestamps are untouched.
            var appliedAtAfter = JoinRows(await QueryRowsAsync(
                database, "SELECT `applied_at` FROM `forge_applied_migration` ORDER BY `migration_file`"));
            Assert.Equal(appliedAtBefore, appliedAtAfter);
        });
    }

    [Fact]
    public async Task InterruptedInstallIsRefusedNotResumed()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var first = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1(), "integration-test");
            Assert.Null(first.ErrorCode);

            // Simulate a run that died mid-install.
            await ExecuteAsync(database, "UPDATE `forge_installed_package` SET `state` = 'installing' WHERE `name` = 'customer-profiles'");

            var second = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1(), "integration-test");

            Assert.Equal(PackageErrorCode.E_INSTALL_INTERRUPTED, second.ErrorCode);
            Assert.Contains("installing", second.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ListReadsTheTrackingTables()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var before = await PackageLister.ListAsync(IntegrationMySql.ConnectionString, database);
            Assert.Null(before.ErrorCode);
            Assert.Empty(before.Value!);

            var install = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1(), "integration-test");
            Assert.Null(install.ErrorCode);

            var listed = await PackageLister.ListAsync(IntegrationMySql.ConnectionString, database);
            Assert.Null(listed.ErrorCode);
            var entry = Assert.Single(listed.Value!);
            Assert.Equal("customer-profiles", entry.Name);
            Assert.Equal("1.0.0", entry.Version);
            Assert.Equal("installed", entry.State);

            // The same values the tracking tables hold, not a derived copy.
            var row = Assert.Single(await QueryRowsAsync(
                database, "SELECT `name`, `version`, `state` FROM `forge_installed_package`"));
            Assert.Equal([entry.Name, entry.Version, entry.State], row);
        });
    }

    private static PreparedPackage CustomerProfilesV1()
    {
        var package = FixturePaths.ContentPackageV1;
        return new PreparedPackage(
            new PackageManifest("customer-profiles", "1.0.0", "Customer profile resources", "example-app", "primary"),
            [
                new MigrationFile(1, "001_customer_profiles.sql", Path.Combine(package, "migrations", "001_customer_profiles.sql")),
                new MigrationFile(2, "002_profile_contacts.sql", Path.Combine(package, "migrations", "002_profile_contacts.sql")),
            ]);
    }

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new MySqlConnection(IntegrationMySql.ConnectionString);
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync(database);
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
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
