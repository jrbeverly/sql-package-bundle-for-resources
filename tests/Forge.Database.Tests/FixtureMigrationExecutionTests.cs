using System.Globalization;
using Forge.Database;
using MySqlConnector;
using Xunit;

namespace Forge.Database.Tests;

[Trait("Category", "Integration")]
public class FixtureMigrationExecutionTests
{
    [Fact]
    public async Task ContentPackageV1MigrationsApplyToAScratchDatabase()
    {
        await RunFixtureMigrationsAsync(
            FixturePaths.ContentPackageV1,
            new Dictionary<string, int> { ["cp_customer_profile"] = 2, ["cp_profile_contact"] = 2 });
    }

    [Fact]
    public async Task ContentPackageV2MigrationsApplyToAScratchDatabase()
    {
        await RunFixtureMigrationsAsync(
            FixturePaths.ContentPackageV2,
            new Dictionary<string, int>
            {
                ["cp_customer_profile"] = 2,
                ["cp_profile_contact"] = 2,
                ["cp_profile_preference"] = 3,
            });
    }

    [Fact]
    public async Task IndependentPackageMigrationsApplyToAScratchDatabase()
    {
        await RunFixtureMigrationsAsync(
            FixturePaths.IndependentPackage,
            new Dictionary<string, int> { ["ae_event_source"] = 2, ["ae_event_type"] = 2 });
    }

    private static async Task RunFixtureMigrationsAsync(string packageDir, IReadOnlyDictionary<string, int> expectedRowCounts)
    {
        var database = $"forge_test_{Guid.NewGuid():N}";
        try
        {
            await ExecuteAdminAsync($"CREATE DATABASE `{database}`");
            var migrations = Directory.GetFiles(Path.Combine(packageDir, "migrations"))
                .OrderBy(f => f, StringComparer.Ordinal);
            foreach (var migration in migrations)
            {
                await MigrationExecutor.ExecuteBatchAsync(TestConnectionString(), database, await File.ReadAllTextAsync(migration));
            }

            foreach (var (table, expectedRows) in expectedRowCounts)
            {
                await AssertTableRowCountAsync(database, table, expectedRows);
            }
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

    private static async Task AssertTableRowCountAsync(string database, string table, int expectedRows)
    {
        await using var connection = new MySqlConnection(TestConnectionString());
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync(database);
        await using var command = new MySqlCommand($"SELECT COUNT(*) FROM `{table}`", connection);
        var actualRows = Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        Assert.Equal(expectedRows, actualRows);
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
}
