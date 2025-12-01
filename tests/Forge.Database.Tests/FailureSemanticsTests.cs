using System.Globalization;
using Forge.Contracts;
using Forge.Database;
using MySqlConnector;
using Xunit;

namespace Forge.Database.Tests;

[Trait("Category", "Integration")]
public class FailureSemanticsTests
{
    [Fact]
    public async Task FailedMigrationStopsTheRunAndMarksThePackageFailed()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var install = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, ReportingPackage(), "integration-test");

            // The failure names the file the server rejected and the server
            // error (SQL.md, Failure Semantics).
            Assert.Equal(PackageErrorCode.E_MIGRATION_FAILED, install.ErrorCode);
            Assert.Contains("002_reports.sql", install.Message, StringComparison.Ordinal);
            Assert.Contains("doesn't exist", install.Message, StringComparison.Ordinal);

            // The package row records the failure.
            var installedRow = Assert.Single(await QueryRowsAsync(
                database, "SELECT `name`, `version`, `state` FROM `forge_installed_package`"));
            Assert.Equal(["reporting-schema", "1.0.0", "failed"], installedRow);

            // Migrations before the failing one are recorded; the failing one
            // and the rest are not.
            Assert.Equal(
                ["001_datasets.sql"],
                (await QueryRowsAsync(database, "SELECT `migration_file` FROM `forge_applied_migration`")).Select(r => r[0]));

            // The run stopped at the failing file: migration 001's content
            // exists, and nothing the later migrations would have created does.
            Assert.Equal(
                ["600001|Sales Summary|60", "600002|Usage Summary|15"],
                JoinRows(await QueryRowsAsync(database, "SELECT `entry`, `name`, `refresh_minutes` FROM `rs_dataset` ORDER BY `entry`")));
            Assert.False(await TrackingTables.ExistsAsync(IntegrationMySql.ConnectionString, database, "rs_report"));
            Assert.False(await TrackingTables.ExistsAsync(IntegrationMySql.ConnectionString, database, "rs_schedule"));
        });
    }

    [Fact]
    public async Task ReRunAfterTheDefectIsFixedAppliesOnlyTheUnrecordedMigrations()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var failed = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, ReportingPackage(), "integration-test");
            Assert.Equal(PackageErrorCode.E_MIGRATION_FAILED, failed.ErrorCode);

            var firstAppliedAt = Assert.Single(await QueryRowsAsync(
                database, "SELECT `applied_at` FROM `forge_applied_migration` WHERE `migration_file` = '001_datasets.sql'"))[0];

            var reRun = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, ReportingPackageFixed(), "integration-test");

            Assert.Null(reRun.ErrorCode);
            Assert.Equal("installed", reRun.Value!.State);

            // All three migrations are recorded...
            Assert.Equal(
                ["001_datasets.sql", "002_reports.sql", "003_schedules.sql"],
                (await QueryRowsAsync(database, "SELECT `migration_file` FROM `forge_applied_migration` ORDER BY `migration_file`")).Select(r => r[0]));

            // ...and the one applied before the failure was not executed
            // again: its row is untouched.
            var firstAppliedAtAfter = Assert.Single(await QueryRowsAsync(
                database, "SELECT `applied_at` FROM `forge_applied_migration` WHERE `migration_file` = '001_datasets.sql'"))[0];
            Assert.Equal(firstAppliedAt, firstAppliedAtAfter);

            // Migration 001's insert is not idempotent, so the dataset rows
            // would be duplicated (and fail on the primary key) if it had
            // run twice. The re-run applied exactly the missing content.
            Assert.Equal(
                ["600001|Sales Summary|60", "600002|Usage Summary|15"],
                JoinRows(await QueryRowsAsync(database, "SELECT `entry`, `name`, `refresh_minutes` FROM `rs_dataset` ORDER BY `entry`")));
            Assert.Equal(
                ["600101|Monthly Sales|600001", "600102|Daily Usage|600002"],
                JoinRows(await QueryRowsAsync(database, "SELECT `entry`, `name`, `dataset_entry` FROM `rs_report` ORDER BY `entry`")));
            Assert.Equal(
                ["600201|Monthly Delivery|600101", "600202|Daily Delivery|600102"],
                JoinRows(await QueryRowsAsync(database, "SELECT `entry`, `name`, `report_entry` FROM `rs_schedule` ORDER BY `entry`")));
        });
    }

    [Fact]
    public async Task InterruptedInstallIsReportedAndExecutesNothing()
    {
        await IntegrationMySql.InScratchDatabaseAsync(async database =>
        {
            var failed = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, ReportingPackage(), "integration-test");
            Assert.Equal(PackageErrorCode.E_MIGRATION_FAILED, failed.ErrorCode);

            // Simulate a run that died mid-install: the row is left in state
            // 'installing', which only an uncontrolled exit produces.
            await ExecuteAsync(database, "UPDATE `forge_installed_package` SET `state` = 'installing' WHERE `name` = 'reporting-schema'");

            var second = await PackageInstaller.InstallAsync(
                IntegrationMySql.ConnectionString, database, ReportingPackageFixed(), "integration-test");

            // Reported, never silently resumed (SQL.md, Installation Tracking).
            Assert.Equal(PackageErrorCode.E_INSTALL_INTERRUPTED, second.ErrorCode);
            Assert.Contains("installing", second.Message, StringComparison.Ordinal);

            // Executes nothing: the applied rows are unchanged and none of
            // the unapplied migrations' content exists.
            Assert.Equal(
                ["001_datasets.sql"],
                (await QueryRowsAsync(database, "SELECT `migration_file` FROM `forge_applied_migration`")).Select(r => r[0]));
            Assert.False(await TrackingTables.ExistsAsync(IntegrationMySql.ConnectionString, database, "rs_report"));
            Assert.False(await TrackingTables.ExistsAsync(IntegrationMySql.ConnectionString, database, "rs_schedule"));
        });
    }

    // The defective fixture, whose middle migration the server rejects, and
    // the fixed copy that stands for the package after the defect is fixed.
    private static PreparedPackage ReportingPackage() => PackageFrom(FixturePaths.ReportingPackage);

    private static PreparedPackage ReportingPackageFixed() => PackageFrom(FixturePaths.ReportingPackageFixed);

    private static PreparedPackage PackageFrom(string packageDir)
    {
        return new PreparedPackage(
            new PackageManifest("reporting-schema", "1.0.0", "Reporting resources: datasets, reports, and schedules", "example-app", "primary"),
            [
                new MigrationFile(1, "001_datasets.sql", Path.Combine(packageDir, "migrations", "001_datasets.sql")),
                new MigrationFile(2, "002_reports.sql", Path.Combine(packageDir, "migrations", "002_reports.sql")),
                new MigrationFile(3, "003_schedules.sql", Path.Combine(packageDir, "migrations", "003_schedules.sql")),
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
