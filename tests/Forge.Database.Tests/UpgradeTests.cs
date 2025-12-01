using System.Globalization;
using Forge.Contracts;
using Forge.Database;
using Forge.Packaging;
using MySqlConnector;
using Xunit;

namespace Forge.Database.Tests;

[Trait("Category", "Integration")]
public class UpgradeTests
{
    [Fact]
    public async Task UpgradeAppliesOnlyTheUnrecordedMigrationAndLeavesEarlierRowsUntouched()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var install = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1(), "integration-test");
            Assert.Null(install.ErrorCode);

            var v1Rows = await QueryRowsAsync(
                database, "SELECT `migration_file`, `package_version`, `migration_digest`, `applied_at` FROM `forge_applied_migration` ORDER BY `migration_file`");

            var upgrade = await PackageUpgrader.UpgradeAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV2());

            Assert.Null(upgrade.ErrorCode);
            Assert.Equal("1.1.0", upgrade.Value!.Package.Version);
            Assert.Equal("installed", upgrade.Value.Package.State);
            Assert.True(upgrade.Value.VersionChanged);

            // The v1 rows are untouched — not re-executed, not re-recorded —
            // and only the new migration was recorded, at the new version.
            var v2Rows = await QueryRowsAsync(
                database, "SELECT `migration_file`, `package_version`, `migration_digest`, `applied_at` FROM `forge_applied_migration` ORDER BY `migration_file`");
            Assert.Equal(3, v2Rows.Count);
            Assert.Equal(v1Rows[0], v2Rows[0]);
            Assert.Equal(v1Rows[1], v2Rows[1]);
            Assert.Equal(["003_profile_preferences.sql", "1.1.0"], [v2Rows[2][0], v2Rows[2][1]]);

            // The earlier content is intact and the new migration's content
            // exists, by direct queries.
            Assert.Equal(
                ["400001|Northwind Supplies|1", "400002|Southridge Services|2"],
                JoinRows(await QueryRowsAsync(database, "SELECT `entry`, `name`, `status` FROM `cp_customer_profile` ORDER BY `entry`")));
            Assert.Equal(
                ["400101|Operations Contact|400001", "400102|Billing Contact|400002"],
                JoinRows(await QueryRowsAsync(database, "SELECT `entry`, `name`, `profile_entry` FROM `cp_profile_contact` ORDER BY `entry`")));
            Assert.Equal(
                ["400201|Email|1", "400202|SMS|2", "400203|Post|3"],
                JoinRows(await QueryRowsAsync(database, "SELECT `entry`, `channel`, `priority` FROM `cp_profile_preference` ORDER BY `entry`")));

            Assert.Equal(
                ["customer-profiles", "1.1.0", "installed"],
                Assert.Single(await QueryRowsAsync(database, "SELECT `name`, `version`, `state` FROM `forge_installed_package`")));
        });
    }

    [Fact]
    public async Task UpgradeToTheSameVersionExecutesNothing()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var install = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1(), "integration-test");
            Assert.Null(install.ErrorCode);

            var appliedBefore = await QueryRowsAsync(
                database, "SELECT `migration_file`, `applied_at` FROM `forge_applied_migration` ORDER BY `migration_file`");

            var upgrade = await PackageUpgrader.UpgradeAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1());

            // SQL.md, Operations, Upgrade, step 2: equal means nothing to do.
            Assert.Null(upgrade.ErrorCode);
            Assert.Equal("1.0.0", upgrade.Value!.Package.Version);
            Assert.False(upgrade.Value.VersionChanged);

            // Executes nothing: the tracking rows are untouched.
            var appliedAfter = await QueryRowsAsync(
                database, "SELECT `migration_file`, `applied_at` FROM `forge_applied_migration` ORDER BY `migration_file`");
            Assert.Equal(appliedBefore, appliedAfter);
            Assert.Equal(
                ["customer-profiles", "1.0.0", "installed"],
                Assert.Single(await QueryRowsAsync(database, "SELECT `name`, `version`, `state` FROM `forge_installed_package`")));
        });
    }

    [Fact]
    public async Task UpgradeToALowerVersionIsRefusedAndExecutesNothing()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var install = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV2(), "integration-test");
            Assert.Null(install.ErrorCode);

            var appliedBefore = await QueryRowsAsync(
                database, "SELECT `migration_file`, `applied_at` FROM `forge_applied_migration` ORDER BY `migration_file`");

            var upgrade = await PackageUpgrader.UpgradeAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1());

            Assert.Equal(PackageErrorCode.E_DOWNGRADE_REFUSED, upgrade.ErrorCode);
            Assert.Contains("1.1.0", upgrade.Message, StringComparison.Ordinal);
            Assert.Contains("1.0.0", upgrade.Message, StringComparison.Ordinal);

            var appliedAfter = await QueryRowsAsync(
                database, "SELECT `migration_file`, `applied_at` FROM `forge_applied_migration` ORDER BY `migration_file`");
            Assert.Equal(appliedBefore, appliedAfter);
            Assert.Equal(
                ["customer-profiles", "1.1.0", "installed"],
                Assert.Single(await QueryRowsAsync(database, "SELECT `name`, `version`, `state` FROM `forge_installed_package`")));
        });
    }

    [Fact]
    public async Task UpgradeOfANotInstalledPackageIsRefusedAndCreatesNothing()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var upgrade = await PackageUpgrader.UpgradeAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1());

            Assert.Equal(PackageErrorCode.E_NOT_INSTALLED, upgrade.ErrorCode);
            Assert.Contains("customer-profiles", upgrade.Message, StringComparison.Ordinal);

            // A refusal before the tracking tables exist leaves the database
            // untouched, tables included.
            Assert.False(await TrackingTables.ExistsAsync(IntegrationMySql.ConnectionString, database, TrackingTables.InstalledPackage));
            Assert.False(await TrackingTables.ExistsAsync(IntegrationMySql.ConnectionString, database, TrackingTables.AppliedMigration));
        });
    }

    [Fact]
    public async Task UpgradeRefusesWhenAnAppliedMigrationWasEdited()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var install = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1(), "integration-test");
            Assert.Null(install.ErrorCode);

            var editedDirectory = Path.Combine(Path.GetTempPath(), $"forge_test_pkg_{Guid.NewGuid():N}");
            CopyDirectory(FixturePaths.ContentPackageV2, editedDirectory);
            await File.AppendAllTextAsync(
                Path.Combine(editedDirectory, "migrations", "001_customer_profiles.sql"),
                "\n-- edited after the 1.0.0 install.\n");

            var upgrade = await PackageUpgrader.UpgradeAsync(
                IntegrationMySql.ConnectionString, database, await ReadPackageAsync(editedDirectory));

            Assert.Equal(PackageErrorCode.E_MIGRATION_MODIFIED, upgrade.ErrorCode);
            Assert.Contains("001_customer_profiles.sql", upgrade.Message, StringComparison.Ordinal);

            // Nothing executed: the v1 rows are untouched, the version is
            // unchanged, and the new migration's content does not exist.
            Assert.Equal(
                ["001_customer_profiles.sql", "002_profile_contacts.sql"],
                (await QueryRowsAsync(database, "SELECT `migration_file` FROM `forge_applied_migration` ORDER BY `migration_file`")).Select(r => r[0]));
            Assert.Equal(
                ["customer-profiles", "1.0.0", "installed"],
                Assert.Single(await QueryRowsAsync(database, "SELECT `name`, `version`, `state` FROM `forge_installed_package`")));
            Assert.False(await TrackingTables.ExistsAsync(IntegrationMySql.ConnectionString, database, "cp_profile_preference"));
        });
    }

    [Fact]
    public async Task UpgradeRefusesWhenARecordedMigrationIsMissingFromTheCandidate()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var install = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1(), "integration-test");
            Assert.Null(install.ErrorCode);

            var prunedDirectory = Path.Combine(Path.GetTempPath(), $"forge_test_pkg_{Guid.NewGuid():N}");
            CopyDirectory(FixturePaths.ContentPackageV2, prunedDirectory);
            File.Delete(Path.Combine(prunedDirectory, "migrations", "001_customer_profiles.sql"));

            var upgrade = await PackageUpgrader.UpgradeAsync(
                IntegrationMySql.ConnectionString, database, await ReadPackageAsync(prunedDirectory));

            Assert.Equal(PackageErrorCode.E_MIGRATION_MODIFIED, upgrade.ErrorCode);
            Assert.Contains("001_customer_profiles.sql", upgrade.Message, StringComparison.Ordinal);

            Assert.Equal(
                ["001_customer_profiles.sql", "002_profile_contacts.sql"],
                (await QueryRowsAsync(database, "SELECT `migration_file` FROM `forge_applied_migration` ORDER BY `migration_file`")).Select(r => r[0]));
            Assert.False(await TrackingTables.ExistsAsync(IntegrationMySql.ConnectionString, database, "cp_profile_preference"));
        });
    }

    [Fact]
    public async Task UpgradeOfAFailedOrInterruptedPackageIsRefusedAndExecutesNothing()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var install = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV1(), "integration-test");
            Assert.Null(install.ErrorCode);

            await ExecuteAsync(database, "UPDATE `forge_installed_package` SET `state` = 'failed' WHERE `name` = 'customer-profiles'");
            var upgradeFailed = await PackageUpgrader.UpgradeAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV2());

            Assert.Equal(PackageErrorCode.E_INSTALL_INTERRUPTED, upgradeFailed.ErrorCode);
            Assert.Contains("failed", upgradeFailed.Message, StringComparison.Ordinal);

            await ExecuteAsync(database, "UPDATE `forge_installed_package` SET `state` = 'installing' WHERE `name` = 'customer-profiles'");
            var upgradeInterrupted = await PackageUpgrader.UpgradeAsync(
                IntegrationMySql.ConnectionString, database, CustomerProfilesV2());

            Assert.Equal(PackageErrorCode.E_INSTALL_INTERRUPTED, upgradeInterrupted.ErrorCode);
            Assert.Contains("installing", upgradeInterrupted.Message, StringComparison.Ordinal);

            Assert.Equal(
                ["001_customer_profiles.sql", "002_profile_contacts.sql"],
                (await QueryRowsAsync(database, "SELECT `migration_file` FROM `forge_applied_migration` ORDER BY `migration_file`")).Select(r => r[0]));
            Assert.False(await TrackingTables.ExistsAsync(IntegrationMySql.ConnectionString, database, "cp_profile_preference"));
        });
    }

    [Fact]
    public async Task UpgradedDatabaseMatchesHandAppliedV2Migrations()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async installedDatabase =>
        {
            var install = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, installedDatabase, CustomerProfilesV1(), "integration-test");
            Assert.Null(install.ErrorCode);

            var upgrade = await PackageUpgrader.UpgradeAsync(
                IntegrationMySql.ConnectionString, installedDatabase, CustomerProfilesV2());
            Assert.Null(upgrade.ErrorCode);

            var handDatabase = $"{installedDatabase}_hand";
            try
            {
                await IntegrationMySql.ExecuteAdminAsync($"CREATE DATABASE `{handDatabase}`");

                // What the mysql client does with each file: send it as one
                // multi-statement batch. The client binary is not installed on
                // the harness, so the same bytes go through MySqlConnector.
                var files = Directory.GetFiles(Path.Combine(FixturePaths.ContentPackageV2, "migrations"))
                    .OrderBy(f => f, StringComparer.Ordinal);
                foreach (var file in files)
                {
                    await MigrationExecutor.ExecuteBatchAsync(
                        IntegrationMySql.ConnectionString, handDatabase, await File.ReadAllTextAsync(file));
                }

                foreach (var table in new[] { "cp_customer_profile", "cp_profile_contact", "cp_profile_preference" })
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

    private static PreparedPackage CustomerProfilesV2()
    {
        var package = FixturePaths.ContentPackageV2;
        return new PreparedPackage(
            new PackageManifest("customer-profiles", "1.1.0", "Customer profile resources", "example-app", "primary"),
            [
                new MigrationFile(1, "001_customer_profiles.sql", Path.Combine(package, "migrations", "001_customer_profiles.sql")),
                new MigrationFile(2, "002_profile_contacts.sql", Path.Combine(package, "migrations", "002_profile_contacts.sql")),
                new MigrationFile(3, "003_profile_preferences.sql", Path.Combine(package, "migrations", "003_profile_preferences.sql")),
            ]);
    }

    private static async Task<PreparedPackage> ReadPackageAsync(string packageDir)
    {
        var read = await PackageReader.ReadAsync(packageDir, "example-app");
        Assert.Null(read.ErrorCode);
        return read.Value!;
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.GetDirectories(source))
        {
            CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
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
