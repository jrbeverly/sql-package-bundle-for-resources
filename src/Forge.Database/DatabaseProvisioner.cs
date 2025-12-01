using Forge.Contracts;
using MySqlConnector;

namespace Forge.Database;

// Creates and drops the databases a realm deployment owns (DEPLOY.md,
// Environment Components). Provisioning runs against whatever database the
// connection string names; CREATE DATABASE does not need a specific context.
public static class DatabaseProvisioner
{
    public static async Task<RealmResult<string>> CreateDatabaseAsync(
        string connectionString, string database, CancellationToken cancellationToken = default)
    {
        var builder = new MySqlConnectionStringBuilder(MigrationExecutor.WithAllowUserVariables(connectionString));
        try
        {
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            // IF NOT EXISTS keeps re-deploy idempotent: the realm's own
            // databases survive a re-deploy and are reused.
            await using var command = new MySqlCommand($"CREATE DATABASE IF NOT EXISTS `{database}`", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return RealmResult<string>.Success(database);
        }
        catch (MySqlException e)
        {
            return RealmResult<string>.Failure(
                RealmErrorCode.E_PROVISION_FAILED, $"could not create database '{database}': {e.Message}");
        }
    }

    public static async Task<RealmResult<string>> DropDatabaseAsync(
        string connectionString, string database, CancellationToken cancellationToken = default)
    {
        var builder = new MySqlConnectionStringBuilder(MigrationExecutor.WithAllowUserVariables(connectionString));
        try
        {
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand($"DROP DATABASE IF EXISTS `{database}`", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return RealmResult<string>.Success(database);
        }
        catch (MySqlException e)
        {
            return RealmResult<string>.Failure(
                RealmErrorCode.E_PROVISION_FAILED, $"could not drop database '{database}': {e.Message}");
        }
    }
}
