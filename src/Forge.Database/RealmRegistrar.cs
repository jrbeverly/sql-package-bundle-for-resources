using Forge.Contracts;
using MySqlConnector;

namespace Forge.Database;

// Writes one realmlist row in the shared authentication database (DEPLOY.md,
// Registration). The PoC targets this one known schema and writes the row;
// schema inspection is production hardening (POC.md Cuts). Registration is
// idempotent per realm id: deploying the same realm twice updates the row
// rather than inserting a second one.
public static class RealmRegistrar
{
    private const string UpsertSql = """
        INSERT INTO `realmlist` (`id`, `name`, `address`, `port`, `icon`, `timezone`, `population`)
        VALUES (@id, @name, @address, @port, @icon, @timezone, @population)
        ON DUPLICATE KEY UPDATE
            `name` = VALUES(`name`),
            `address` = VALUES(`address`),
            `port` = VALUES(`port`),
            `icon` = VALUES(`icon`),
            `timezone` = VALUES(`timezone`),
            `population` = VALUES(`population`)
        """;

    public static async Task<RealmResult<int>> RegisterAsync(
        string connectionString,
        string authDatabase,
        RealmRegistration registration,
        CancellationToken cancellationToken = default)
    {
        var builder = new MySqlConnectionStringBuilder(MigrationExecutor.WithAllowUserVariables(connectionString))
        {
            Database = authDatabase,
        };
        try
        {
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand(UpsertSql, connection);
            command.Parameters.AddWithValue("id", registration.RealmId);
            command.Parameters.AddWithValue("name", registration.Name);
            command.Parameters.AddWithValue("address", registration.Address);
            command.Parameters.AddWithValue("port", registration.Port);
            command.Parameters.AddWithValue("icon", registration.Icon);
            command.Parameters.AddWithValue("timezone", registration.Timezone);
            command.Parameters.AddWithValue("population", registration.Population);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return RealmResult<int>.Success(registration.RealmId);
        }
        catch (MySqlException e)
        {
            return RealmResult<int>.Failure(
                RealmErrorCode.E_REGISTRATION_FAILED,
                $"could not register realm {registration.RealmId} in '{authDatabase}': {e.Message}");
        }
    }
}
