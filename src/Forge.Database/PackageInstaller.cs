using Forge.Contracts;
using MySqlConnector;

namespace Forge.Database;

// Installs a prepared package: the tracking tables, the installing row, each
// migration, then the installed row (SQL.md, Operations, Install). A failed
// migration marks the row failed and keeps the applied records; a later run
// resumes at the first unrecorded migration (SQL.md, Failure Semantics).
public static class PackageInstaller
{
    private const string InsertInstallingSql = """
        INSERT INTO `forge_installed_package`
            (`name`, `version`, `target_core`, `installed_at`, `installed_by`, `state`)
        VALUES (@name, @version, @target_core, @installed_at, @installed_by, @state)
        """;

    private const string UpdateStateSql = """
        UPDATE `forge_installed_package` SET `state` = @state WHERE `name` = @name
        """;

    private const string SelectInstalledSql = """
        SELECT `version`, `state`, `installed_at` FROM `forge_installed_package` WHERE `name` = @name
        """;

    private const string SelectAppliedSql = """
        SELECT `migration_file` FROM `forge_applied_migration` WHERE `package_name` = @package_name
        """;

    public static async Task<PackageResult<InstalledPackage>> InstallAsync(
        string connectionString,
        string database,
        PreparedPackage package,
        string installedBy,
        CancellationToken cancellationToken = default)
    {
        var manifest = package.Manifest;

        // The already-installed check runs before the tracking tables are
        // created, so a refusal here executes no writes (SQL.md, Operations,
        // Install, steps 4 and 7).
        var installedResult = await ReadInstalledAsync(connectionString, database, manifest.Name, cancellationToken);
        if (installedResult.ErrorCode is not null)
        {
            return PackageResult<InstalledPackage>.Failure(installedResult.ErrorCode.Value, installedResult.Message!);
        }

        var installed = installedResult.Value;
        var applied = new HashSet<string>(StringComparer.Ordinal);
        if (installed is not null)
        {
            if (installed.State == TrackingTables.StateInstalled)
            {
                return PackageResult<InstalledPackage>.Failure(
                    PackageErrorCode.E_ALREADY_INSTALLED,
                    $"package '{manifest.Name}' is already installed at version {installed.Version}");
            }

            // SQL.md, Installation Tracking: an interrupted install is
            // reported, never silently resumed.
            if (installed.State != TrackingTables.StateFailed)
            {
                return PackageResult<InstalledPackage>.Failure(
                    PackageErrorCode.E_INSTALL_INTERRUPTED,
                    $"package '{manifest.Name}' is in state '{installed.State}' from an interrupted install");
            }

            // A failed row is the recoverable marker left by an earlier run:
            // resume at the first unrecorded migration (SQL.md, Failure
            // Semantics).
            var appliedResult = await ReadAppliedMigrationsAsync(connectionString, database, manifest.Name, cancellationToken);
            if (appliedResult.ErrorCode is not null)
            {
                return PackageResult<InstalledPackage>.Failure(appliedResult.ErrorCode.Value, appliedResult.Message!);
            }

            applied = appliedResult.Value!;
        }
        else
        {
            var createFailure = await ExecuteToolSqlAsync(
                connectionString, database, TrackingTables.CreateSql, "create the tracking tables", cancellationToken);
            if (createFailure is not null)
            {
                return PackageResult<InstalledPackage>.Failure(createFailure.Value.Code, createFailure.Value.Message);
            }

            var insertFailure = await ExecuteToolSqlAsync(
                connectionString, database, InsertInstallingSql, "record the package as installing", cancellationToken,
                ("name", manifest.Name), ("version", manifest.Version), ("target_core", manifest.TargetCore),
                ("installed_at", DateTime.UtcNow), ("installed_by", installedBy), ("state", TrackingTables.StateInstalling));
            if (insertFailure is not null)
            {
                return PackageResult<InstalledPackage>.Failure(insertFailure.Value.Code, insertFailure.Value.Message);
            }
        }

        foreach (var migration in package.Migrations)
        {
            if (applied.Contains(migration.FileName))
            {
                continue;
            }

            var migrationResult = await MigrationExecutor.ExecuteMigrationAsync(
                connectionString, database, migration, manifest.Name, manifest.Version, cancellationToken);
            if (migrationResult.ErrorCode is not null)
            {
                // SQL.md, Failure Semantics: the row records the failure and
                // keeps the successfully applied records; a re-run resumes at
                // the first unrecorded migration. Marking is best-effort —
                // the failure the operator acts on is the migration's.
                _ = await ExecuteToolSqlAsync(
                    connectionString, database, UpdateStateSql, "mark the package failed", cancellationToken,
                    ("state", TrackingTables.StateFailed), ("name", manifest.Name));
                return PackageResult<InstalledPackage>.Failure(migrationResult.ErrorCode.Value, migrationResult.Message!);
            }
        }

        var markFailure = await ExecuteToolSqlAsync(
            connectionString, database, UpdateStateSql, "mark the package installed", cancellationToken,
            ("state", TrackingTables.StateInstalled), ("name", manifest.Name));
        if (markFailure is not null)
        {
            return PackageResult<InstalledPackage>.Failure(markFailure.Value.Code, markFailure.Value.Message);
        }

        return PackageResult<InstalledPackage>.Success(
            new InstalledPackage(manifest.Name, manifest.Version, TrackingTables.StateInstalled, DateTime.UtcNow));
    }

    private static async Task<PackageResult<InstalledPackage?>> ReadInstalledAsync(
        string connectionString, string database, string packageName, CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(MigrationExecutor.WithAllowUserVariables(connectionString))
        {
            Database = database,
        };
        try
        {
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            if (!await TrackingTables.ExistsAsync(connectionString, database, TrackingTables.InstalledPackage, cancellationToken))
            {
                return PackageResult<InstalledPackage?>.Success(null);
            }

            await using var select = new MySqlCommand(SelectInstalledSql, connection);
            select.Parameters.AddWithValue("name", packageName);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return PackageResult<InstalledPackage?>.Success(null);
            }

            return PackageResult<InstalledPackage?>.Success(new InstalledPackage(
                packageName, reader.GetString(0), reader.GetString(1), reader.GetDateTime(2)));
        }
        catch (MySqlException e)
        {
            return PackageResult<InstalledPackage?>.Failure(
                PackageErrorCode.E_MIGRATION_FAILED, $"could not read the tracking tables: {e.Message}");
        }
    }

    // The migration files recorded for a package (SQL.md, Installation
    // Tracking). The tracking tables exist whenever a package row does, so
    // this runs only on the resume path after a failed install.
    private static async Task<PackageResult<HashSet<string>>> ReadAppliedMigrationsAsync(
        string connectionString, string database, string packageName, CancellationToken cancellationToken)
    {
        var builder = new MySqlConnectionStringBuilder(MigrationExecutor.WithAllowUserVariables(connectionString))
        {
            Database = database,
        };
        try
        {
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand(SelectAppliedSql, connection);
            command.Parameters.AddWithValue("package_name", packageName);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var applied = new HashSet<string>(StringComparer.Ordinal);
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0));
            }

            return PackageResult<HashSet<string>>.Success(applied);
        }
        catch (MySqlException e)
        {
            return PackageResult<HashSet<string>>.Failure(
                PackageErrorCode.E_MIGRATION_FAILED, $"could not read the tracking tables: {e.Message}");
        }
    }

    private static async Task<(PackageErrorCode Code, string Message)?> ExecuteToolSqlAsync(
        string connectionString,
        string database,
        string sql,
        string failureAction,
        CancellationToken cancellationToken,
        params (string Name, object Value)[] parameters)
    {
        var builder = new MySqlConnectionStringBuilder(MigrationExecutor.WithAllowUserVariables(connectionString))
        {
            Database = database,
        };
        try
        {
            await using var connection = new MySqlConnection(builder.ConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new MySqlCommand(sql, connection);
            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await command.ExecuteNonQueryAsync(cancellationToken);
            return null;
        }
        catch (MySqlException e)
        {
            return (PackageErrorCode.E_MIGRATION_FAILED, $"could not {failureAction}: {e.Message}");
        }
    }
}
