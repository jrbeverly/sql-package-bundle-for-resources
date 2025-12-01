using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Forge.Contracts;
using MySqlConnector;

namespace Forge.Database;

public static class MigrationExecutor
{
    // Lowercase hex SHA-256 of file bytes (SQL.md, Installation Tracking).
    public static string DigestOf(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // AllowUserVariables must be on, or @var assignments across statements are
    // rejected by the server (TECHNICAL.md, Database Execution).
    public static string WithAllowUserVariables(string connectionString)
    {
        var builder = new MySqlConnectionStringBuilder(connectionString)
        {
            AllowUserVariables = true,
        };
        return builder.ConnectionString;
    }

    // One connection per migration file: session state (user variables, sql_mode)
    // must not leak from one migration into the next (TECHNICAL.md, Connection lifetime).
    public static async Task ExecuteBatchAsync(
        string connectionString,
        string database,
        string sql,
        CancellationToken cancellationToken = default)
    {
        var builder = new MySqlConnectionStringBuilder(WithAllowUserVariables(connectionString))
        {
            Database = database,
        };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new MySqlCommand(sql, connection)
        {
            // Large content imports legitimately run for minutes (TECHNICAL.md, Timeouts).
            CommandTimeout = 0,
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // Executes one migration file as a single multi-statement batch inside a
    // transaction the tool opens, then records the migration in the same
    // transaction where the database allows it (SQL.md, Execution;
    // Installation Tracking). The value is the migration's digest.
    public static async Task<PackageResult<string>> ExecuteMigrationAsync(
        string connectionString,
        string database,
        MigrationFile migration,
        string packageName,
        string packageVersion,
        CancellationToken cancellationToken = default)
    {
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(migration.Path, cancellationToken);
        }
        catch (Exception e)
        {
            return PackageResult<string>.Failure(
                PackageErrorCode.E_MIGRATION_FAILED, $"migration '{migration.FileName}' could not be read: {e.Message}");
        }

        var digest = DigestOf(bytes);
        var sql = Encoding.UTF8.GetString(bytes);
        var builder = new MySqlConnectionStringBuilder(WithAllowUserVariables(connectionString))
        {
            Database = database,
        };
        await using var connection = new MySqlConnection(builder.ConnectionString);
        MySqlTransaction? transaction = null;
        try
        {
            await connection.OpenAsync(cancellationToken);
            transaction = await connection.BeginTransactionAsync(cancellationToken);
            var stopwatch = Stopwatch.StartNew();
            await using (var command = new MySqlCommand(sql, connection, transaction)
            {
                // Large content imports legitimately run for minutes (TECHNICAL.md, Timeouts).
                CommandTimeout = 0,
            })
            {
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            stopwatch.Stop();
            await using (var record = new MySqlCommand(
                "INSERT INTO `forge_applied_migration` " +
                "(`package_name`, `migration_file`, `package_version`, `migration_digest`, `applied_at`, `duration_ms`) " +
                "VALUES (@package_name, @migration_file, @package_version, @migration_digest, @applied_at, @duration_ms)",
                connection, transaction))
            {
                record.Parameters.AddWithValue("package_name", packageName);
                record.Parameters.AddWithValue("migration_file", migration.FileName);
                record.Parameters.AddWithValue("package_version", packageVersion);
                record.Parameters.AddWithValue("migration_digest", digest);
                record.Parameters.AddWithValue("applied_at", DateTime.UtcNow);
                record.Parameters.AddWithValue("duration_ms", stopwatch.ElapsedMilliseconds);
                await record.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return PackageResult<string>.Success(digest);
        }
        catch (MySqlException e)
        {
            if (transaction is not null)
            {
                // DDL in the batch commits implicitly and ends the transaction,
                // so the rollback may be a no-op and the connection may be gone.
                try
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                }
                catch (MySqlException)
                {
                }
            }

            return PackageResult<string>.Failure(
                PackageErrorCode.E_MIGRATION_FAILED, $"migration '{migration.FileName}' failed: {e.Message}");
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
