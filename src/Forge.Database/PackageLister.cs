using Forge.Contracts;
using MySqlConnector;

namespace Forge.Database;

// Reads installed packages from the tracking tables alone (SQL.md,
// Operations, List).
public static class PackageLister
{
    private const string ListSql = """
        SELECT `name`, `version`, `state`, `installed_at` FROM `forge_installed_package` ORDER BY `name`
        """;

    public static async Task<PackageResult<IReadOnlyList<InstalledPackage>>> ListAsync(
        string connectionString, string database, CancellationToken cancellationToken = default)
    {
        var builder = new MySqlConnectionStringBuilder(MigrationExecutor.WithAllowUserVariables(connectionString))
        {
            Database = database,
        };
        try
        {
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            // No tracking tables means no installed packages, not an error.
            if (!await TrackingTables.ExistsAsync(connectionString, database, TrackingTables.InstalledPackage, cancellationToken))
            {
                return PackageResult<IReadOnlyList<InstalledPackage>>.Success(Array.Empty<InstalledPackage>());
            }

            var packages = new List<InstalledPackage>();
            await using var command = new MySqlCommand(ListSql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                packages.Add(new InstalledPackage(
                    reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetDateTime(3)));
            }

            return PackageResult<IReadOnlyList<InstalledPackage>>.Success(packages);
        }
        catch (MySqlException e)
        {
            return PackageResult<IReadOnlyList<InstalledPackage>>.Failure(
                PackageErrorCode.E_MIGRATION_FAILED, $"could not read the tracking tables: {e.Message}");
        }
    }
}
