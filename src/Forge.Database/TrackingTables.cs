using System.Globalization;
using MySqlConnector;

namespace Forge.Database;

// The installation tracking tables (SQL.md, Installation Tracking), without
// the package_hash column, whose computation POC.md cuts. These tables are
// the tool's; no core schema references them.
public static class TrackingTables
{
    public const string InstalledPackage = "forge_installed_package";

    public const string AppliedMigration = "forge_applied_migration";

    public const string StateInstalling = "installing";

    public const string StateInstalled = "installed";

    public const string StateFailed = "failed";

    internal const string CreateSql = """
        CREATE TABLE IF NOT EXISTS `forge_installed_package` (
            `name` VARCHAR(64) NOT NULL,
            `version` VARCHAR(64) NOT NULL,
            `target_core` VARCHAR(128) NOT NULL,
            `installed_at` DATETIME NOT NULL,
            `installed_by` VARCHAR(128) NOT NULL,
            `state` VARCHAR(16) NOT NULL,
            PRIMARY KEY (`name`)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

        CREATE TABLE IF NOT EXISTS `forge_applied_migration` (
            `package_name` VARCHAR(64) NOT NULL,
            `migration_file` VARCHAR(255) NOT NULL,
            `package_version` VARCHAR(64) NOT NULL,
            `migration_digest` CHAR(64) NOT NULL,
            `applied_at` DATETIME NOT NULL,
            `duration_ms` INT UNSIGNED NOT NULL,
            PRIMARY KEY (`package_name`, `migration_file`),
            CONSTRAINT `fk_applied_package`
                FOREIGN KEY (`package_name`) REFERENCES `forge_installed_package` (`name`)
                ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
        """;

    public static async Task<bool> ExistsAsync(
        string connectionString, string database, string table, CancellationToken cancellationToken = default)
    {
        var builder = new MySqlConnectionStringBuilder(MigrationExecutor.WithAllowUserVariables(connectionString))
        {
            Database = database,
        };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = @table_name",
            connection);
        command.Parameters.AddWithValue("table_name", table);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
    }
}
