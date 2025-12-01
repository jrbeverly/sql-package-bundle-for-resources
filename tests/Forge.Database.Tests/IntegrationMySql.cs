using MySqlConnector;

namespace Forge.Database.Tests;

// Connection plumbing for integration tests: the FORGE_TEST_MYSQL guard and
// admin-level statements on the harness server (TECHNICAL.md, Testing).
internal static class IntegrationMySql
{
    public static string ConnectionString
    {
        get
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

    public static async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public static async Task InScratchDatabaseAsync(Func<string, Task> test)
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
}
