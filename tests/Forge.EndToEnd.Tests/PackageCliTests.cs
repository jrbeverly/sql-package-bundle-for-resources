using System.Diagnostics;
using System.Globalization;
using MySqlConnector;
using Xunit;

namespace Forge.EndToEnd.Tests;

[Trait("Category", "Integration")]
public class PackageCliTests
{
    [Fact]
    public async Task InstallAndListThroughTheCliLeaveTrackedState()
    {
        await InScratchDatabaseAsync(async database =>
        {
            var config = await WriteConfigAsync(database);

            var install = await RunForgeAsync("package", "install", CliPaths.ContentPackageV1, "--config", config);

            Assert.Equal(0, install.ExitCode);
            Assert.Contains("installed customer-profiles 1.0.0", install.StdOut, StringComparison.Ordinal);

            // Not only the tool output: the database itself answers.
            var row = Assert.Single(await QueryRowsAsync(
                database, "SELECT `name`, `version`, `state` FROM `forge_installed_package`"));
            Assert.Equal(["customer-profiles", "1.0.0", "installed"], row);
            Assert.Equal(2, (await QueryRowsAsync(database, "SELECT * FROM `forge_applied_migration`")).Count);
            Assert.Equal(2, (await QueryRowsAsync(database, "SELECT * FROM `cp_customer_profile`")).Count);

            var list = await RunForgeAsync("package", "list", "--config", config);

            Assert.Equal(0, list.ExitCode);
            Assert.Contains("customer-profiles 1.0.0 installed", list.StdOut, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task UpgradeThroughTheCliAppliesOnlyTheNewMigration()
    {
        await InScratchDatabaseAsync(async database =>
        {
            var config = await WriteConfigAsync(database);

            var install = await RunForgeAsync("package", "install", CliPaths.ContentPackageV1, "--config", config);
            Assert.Equal(0, install.ExitCode);

            var upgrade = await RunForgeAsync("package", "upgrade", CliPaths.ContentPackageV2, "--config", config);

            Assert.Equal(0, upgrade.ExitCode);
            Assert.Contains("upgraded customer-profiles to 1.1.0", upgrade.StdOut, StringComparison.Ordinal);

            // The v1 rows keep their recorded version; only the new migration
            // was recorded, at the new version, and the row moved.
            Assert.Equal(
                ["1.0.0", "1.0.0", "1.1.0"],
                (await QueryRowsAsync(database, "SELECT `package_version` FROM `forge_applied_migration` ORDER BY `migration_file`")).Select(r => r[0]));
            Assert.Equal(
                ["customer-profiles", "1.1.0", "installed"],
                Assert.Single(await QueryRowsAsync(database, "SELECT `name`, `version`, `state` FROM `forge_installed_package`")));
            Assert.Equal(3, (await QueryRowsAsync(database, "SELECT * FROM `cp_profile_preference`")).Count);

            // Upgrading to the same version executes nothing and says so.
            var sameVersion = await RunForgeAsync("package", "upgrade", CliPaths.ContentPackageV2, "--config", config);

            Assert.Equal(0, sameVersion.ExitCode);
            Assert.Contains("already at version 1.1.0; nothing to do", sameVersion.StdOut, StringComparison.Ordinal);
            Assert.Equal(3, (await QueryRowsAsync(database, "SELECT * FROM `forge_applied_migration`")).Count);
        });
    }

    [Fact]
    public async Task DowngradeThroughTheCliIsRefused()
    {
        await InScratchDatabaseAsync(async database =>
        {
            var config = await WriteConfigAsync(database);

            var install = await RunForgeAsync("package", "install", CliPaths.ContentPackageV2, "--config", config);
            Assert.Equal(0, install.ExitCode);

            var downgrade = await RunForgeAsync("package", "upgrade", CliPaths.ContentPackageV1, "--config", config);

            Assert.Equal(13, downgrade.ExitCode);
            Assert.Contains("E_DOWNGRADE_REFUSED", downgrade.StdErr, StringComparison.Ordinal);
            Assert.Equal(3, (await QueryRowsAsync(database, "SELECT * FROM `forge_applied_migration`")).Count);
            Assert.Equal(
                ["customer-profiles", "1.1.0", "installed"],
                Assert.Single(await QueryRowsAsync(database, "SELECT `name`, `version`, `state` FROM `forge_installed_package`")));
        });
    }

    [Fact]
    public async Task SecondInstallThroughTheCliIsRefused()
    {
        await InScratchDatabaseAsync(async database =>
        {
            var config = await WriteConfigAsync(database);

            var install = await RunForgeAsync("package", "install", CliPaths.ContentPackageV1, "--config", config);
            Assert.Equal(0, install.ExitCode);

            var second = await RunForgeAsync("package", "install", CliPaths.ContentPackageV1, "--config", config);

            Assert.Equal(13, second.ExitCode);
            Assert.Contains("E_ALREADY_INSTALLED", second.StdErr, StringComparison.Ordinal);
            Assert.Equal(2, (await QueryRowsAsync(database, "SELECT * FROM `forge_applied_migration`")).Count);
        });
    }

    [Fact]
    public async Task WrongCorePackageIsRefusedBeforeAnySqlExecutes()
    {
        await InScratchDatabaseAsync(async database =>
        {
            var config = await WriteConfigAsync(database);
            var package = await WriteWrongCorePackageAsync();

            var install = await RunForgeAsync("package", "install", package, "--config", config);

            Assert.Equal(14, install.ExitCode);
            Assert.Contains("E_TARGET_MISMATCH", install.StdErr, StringComparison.Ordinal);

            // No SQL reached the database: the tracking tables were never
            // created, and the package's deliberately invalid migration was
            // never executed.
            Assert.Empty(await QueryRowsAsync(
                database,
                "SELECT * FROM information_schema.tables WHERE table_schema = DATABASE() " +
                "AND table_name IN ('forge_installed_package', 'forge_applied_migration')"));
        });
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunForgeAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = CliPaths.ForgeBinary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"could not start {CliPaths.ForgeBinary}; run the build first (make validate)");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(2));
        return (process.ExitCode, stdout, stderr);
    }

    private static async Task<string> WriteConfigAsync(string database)
    {
        var builder = new MySqlConnectionStringBuilder(TestConnectionString());
        var path = Path.Combine(Path.GetTempPath(), $"forge_test_cfg_{Guid.NewGuid():N}.yaml");
        var yaml = $"""
            apiVersion: forge.v1
            kind: Config

            spec:
              core: example-app
              database:
                host: {builder.Server}
                port: {builder.Port}
                user: {builder.UserID}
                password: {builder.Password}
                primary: {database}
            """;
        await File.WriteAllTextAsync(path, yaml);
        return path;
    }

    private static async Task<string> WriteWrongCorePackageAsync()
    {
        var package = Path.Combine(Path.GetTempPath(), $"forge_test_pkg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(package, "migrations"));
        await File.WriteAllTextAsync(Path.Combine(package, "manifest.yaml"), """
            apiVersion: sqlpackage.v1
            kind: Package

            metadata:
              name: wrong-core
              version: 1.0.0
              description: Targets a core that is not configured

            spec:
              targets:
                core: other-app
                database: primary
            """);

        // Invalid SQL: if this file were ever executed the install would fail
        // with E_MIGRATION_FAILED instead of E_TARGET_MISMATCH.
        await File.WriteAllTextAsync(Path.Combine(package, "migrations", "001_broken.sql"), "THIS IS NOT SQL;");
        return package;
    }

    private static string TestConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("FORGE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "FORGE_TEST_MYSQL is not set. Integration tests need a running MySQL: " +
                "start it with `make db-up`, then set FORGE_TEST_MYSQL to " +
                "\"Server=127.0.0.1;Port=3306;User ID=forge;Password=forge-test;Database=primary\".");
        }

        return connectionString;
    }

    private static async Task InScratchDatabaseAsync(Func<string, Task> test)
    {
        var database = $"forge_test_{Guid.NewGuid():N}";
        try
        {
            await ExecuteAdminAsync($"CREATE DATABASE `{database}`");
            await test(database);
        }
        finally
        {
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS `{database}`");
        }
    }

    private static async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new MySqlConnection(TestConnectionString());
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string[]>> QueryRowsAsync(string database, string sql)
    {
        await using var connection = new MySqlConnection(TestConnectionString());
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
}
