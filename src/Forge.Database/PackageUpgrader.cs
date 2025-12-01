using Forge.Contracts;
using MySqlConnector;

namespace Forge.Database;

// Upgrades an installed package: an equal version is a no-op, a lower one is
// refused, and a higher one first verifies that every recorded migration
// still has a matching digest in the candidate, then applies only the
// unrecorded migrations in order and records the new version (SQL.md,
// Operations, Upgrade). Migrations already applied are never re-run.
public static class PackageUpgrader
{
    private const string SelectInstalledSql = """
        SELECT `version`, `state`, `installed_at` FROM `forge_installed_package` WHERE `name` = @name
        """;

    private const string SelectAppliedSql = """
        SELECT `migration_file`, `migration_digest` FROM `forge_applied_migration` WHERE `package_name` = @package_name
        """;

    private const string UpdateVersionSql = """
        UPDATE `forge_installed_package` SET `version` = @version WHERE `name` = @name
        """;

    private const string UpdateVersionAndStateSql = """
        UPDATE `forge_installed_package` SET `version` = @version, `state` = @state WHERE `name` = @name
        """;

    public static async Task<PackageResult<PackageUpgrade>> UpgradeAsync(
        string connectionString,
        string database,
        PreparedPackage package,
        CancellationToken cancellationToken = default)
    {
        var manifest = package.Manifest;

        var installedResult = await ReadInstalledAsync(connectionString, database, manifest.Name, cancellationToken);
        if (installedResult.ErrorCode is not null)
        {
            return PackageResult<PackageUpgrade>.Failure(installedResult.ErrorCode.Value, installedResult.Message!);
        }

        if (installedResult.Value is null)
        {
            return PackageResult<PackageUpgrade>.Failure(
                PackageErrorCode.E_NOT_INSTALLED, $"package '{manifest.Name}' is not installed");
        }

        var installed = installedResult.Value;
        if (installed.State != TrackingTables.StateInstalled)
        {
            // SQL.md, Installation Tracking: a row in state 'installing' means
            // a previous run died mid-install and is reported, never silently
            // resumed. Upgrade refuses any state other than 'installed'; a
            // failed install is recovered by re-running install, not by
            // upgrading.
            return PackageResult<PackageUpgrade>.Failure(
                PackageErrorCode.E_INSTALL_INTERRUPTED,
                $"package '{manifest.Name}' is in state '{installed.State}', not installed; upgrade requires an installed package");
        }

        var installedVersion = SemanticVersion.Parse(installed.Version);
        if (installedVersion is null)
        {
            // Only a hand-edited row gets here: the tool records only
            // manifest-validated versions.
            return PackageResult<PackageUpgrade>.Failure(
                PackageErrorCode.E_INSTALL_INTERRUPTED,
                $"package '{manifest.Name}' records version '{installed.Version}', which is not a SemVer 2.0.0 version");
        }

        var candidateVersion = SemanticVersion.Parse(manifest.Version);
        if (candidateVersion is null)
        {
            // PackageReader validates metadata.version before this, so this
            // is unreachable through the CLI.
            return PackageResult<PackageUpgrade>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID,
                $"package '{manifest.Name}' version '{manifest.Version}' is not a SemVer 2.0.0 version");
        }

        var comparison = candidateVersion.CompareTo(installedVersion);
        if (comparison == 0)
        {
            // SQL.md, Operations, Upgrade, step 2: nothing to do.
            return PackageResult<PackageUpgrade>.Success(new PackageUpgrade(installed, false));
        }

        if (comparison < 0)
        {
            return PackageResult<PackageUpgrade>.Failure(
                PackageErrorCode.E_DOWNGRADE_REFUSED,
                $"package '{manifest.Name}' is installed at version {installed.Version}, which is higher than the candidate version {manifest.Version}; a downgrade is remove then install");
        }

        var appliedResult = await ReadAppliedAsync(connectionString, database, manifest.Name, cancellationToken);
        if (appliedResult.ErrorCode is not null)
        {
            return PackageResult<PackageUpgrade>.Failure(appliedResult.ErrorCode.Value, appliedResult.Message!);
        }

        // SQL.md, Operations, Upgrade, step 3: every recorded migration must
        // still have a matching digest in the candidate package, or the
        // database's history no longer matches the package's.
        foreach (var (file, recordedDigest) in appliedResult.Value!)
        {
            var candidate = package.Migrations.FirstOrDefault(m => m.FileName == file);
            if (candidate is null)
            {
                return PackageResult<PackageUpgrade>.Failure(
                    PackageErrorCode.E_MIGRATION_MODIFIED,
                    $"migration '{file}' is recorded as applied to package '{manifest.Name}' but is not present in the candidate package");
            }

            var digestResult = await ReadDigestAsync(candidate, cancellationToken);
            if (digestResult.ErrorCode is not null)
            {
                return PackageResult<PackageUpgrade>.Failure(digestResult.ErrorCode.Value, digestResult.Message!);
            }

            if (!string.Equals(recordedDigest, digestResult.Value, StringComparison.Ordinal))
            {
                return PackageResult<PackageUpgrade>.Failure(
                    PackageErrorCode.E_MIGRATION_MODIFIED,
                    $"migration '{file}' was modified after it was applied; the recorded digest does not match the candidate package's copy");
            }
        }

        var applied = new HashSet<string>(appliedResult.Value!.Keys, StringComparer.Ordinal);
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
                // the version it was attempting, and the applied records
                // stay. Marking is best-effort — the failure the operator
                // acts on is the migration's.
                _ = await ExecuteToolSqlAsync(
                    connectionString, database, UpdateVersionAndStateSql, "mark the package failed", cancellationToken,
                    ("version", manifest.Version), ("state", TrackingTables.StateFailed), ("name", manifest.Name));
                return PackageResult<PackageUpgrade>.Failure(migrationResult.ErrorCode.Value, migrationResult.Message!);
            }
        }

        var markFailure = await ExecuteToolSqlAsync(
            connectionString, database, UpdateVersionSql, "record the upgraded version", cancellationToken,
            ("version", manifest.Version), ("name", manifest.Name));
        if (markFailure is not null)
        {
            return PackageResult<PackageUpgrade>.Failure(markFailure.Value.Code, markFailure.Value.Message);
        }

        // The row keeps its original install time: an upgrade is not a new
        // install.
        return PackageResult<PackageUpgrade>.Success(new PackageUpgrade(
            new InstalledPackage(manifest.Name, manifest.Version, TrackingTables.StateInstalled, installed.InstalledAt),
            true));
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

    // The migration files recorded for a package, with their digests (SQL.md,
    // Installation Tracking).
    private static async Task<PackageResult<Dictionary<string, string>>> ReadAppliedAsync(
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
            var applied = new Dictionary<string, string>(StringComparer.Ordinal);
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0), reader.GetString(1));
            }

            return PackageResult<Dictionary<string, string>>.Success(applied);
        }
        catch (MySqlException e)
        {
            return PackageResult<Dictionary<string, string>>.Failure(
                PackageErrorCode.E_MIGRATION_FAILED, $"could not read the tracking tables: {e.Message}");
        }
    }

    private static async Task<PackageResult<string>> ReadDigestAsync(
        MigrationFile migration, CancellationToken cancellationToken)
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

        return PackageResult<string>.Success(MigrationExecutor.DigestOf(bytes));
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
