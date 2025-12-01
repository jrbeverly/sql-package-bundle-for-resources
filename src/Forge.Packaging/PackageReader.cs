using System.Globalization;
using System.Text.RegularExpressions;
using Forge.Contracts;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Forge.Packaging;

// Reads a package directory into an install plan: manifest validation, then
// migration discovery, then target matching (SQL.md, Operations, Install).
// Nothing here touches a database, so every refusal below happens before any
// SQL can execute.
public static class PackageReader
{
    // SQL.md, Manifest: metadata.name.
    private static readonly Regex NamePattern = new("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.Compiled);

    // The SemVer 2.0.0 grammar from semver.org, used for metadata.version.
    private static readonly Regex SemVerPattern = new(
        "^(0|[1-9]\\d*)\\.(0|[1-9]\\d*)\\.(0|[1-9]\\d*)(?:-((?:0|[1-9]\\d*|\\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\\.(?:0|[1-9]\\d*|\\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\\+([0-9a-zA-Z-]+(?:\\.[0-9a-zA-Z-]+)*))?$",
        RegexOptions.Compiled);

    // SQL.md, Migrations: <NNN>_<slug>.sql. The slug also accepts _, which
    // SQL.md's [a-z0-9-]* grammar omits but the committed fixture corpus uses.
    private static readonly Regex MigrationNamePattern = new("^([0-9]{3,})_([a-z0-9][a-z0-9_-]*)\\.sql$", RegexOptions.Compiled);

    private static readonly string[] TargetDatabases = ["primary", "analytics", "identity"];

    public static async Task<PackageResult<PreparedPackage>> ReadAsync(
        string packagePath,
        string configuredCore,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(packagePath))
        {
            return PackageResult<PreparedPackage>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"package directory '{packagePath}' does not exist");
        }

        var manifestResult = await ReadManifestAsync(packagePath, cancellationToken);
        if (manifestResult.ErrorCode is not null)
        {
            return PackageResult<PreparedPackage>.Failure(manifestResult.ErrorCode.Value, manifestResult.Message!);
        }

        var migrationsResult = DiscoverMigrations(packagePath);
        if (migrationsResult.ErrorCode is not null)
        {
            return PackageResult<PreparedPackage>.Failure(migrationsResult.ErrorCode.Value, migrationsResult.Message!);
        }

        var manifest = manifestResult.Value!;
        if (!string.Equals(manifest.TargetCore, configuredCore, StringComparison.Ordinal))
        {
            // SQL.md, Target Matching: exact string equality against the
            // configured core, never inferred from the value.
            return PackageResult<PreparedPackage>.Failure(
                PackageErrorCode.E_TARGET_MISMATCH,
                $"package '{manifest.Name}' targets core '{manifest.TargetCore}' but the configured core is '{configuredCore}'");
        }

        return PackageResult<PreparedPackage>.Success(new PreparedPackage(manifest, migrationsResult.Value!));
    }

    private static async Task<PackageResult<PackageManifest>> ReadManifestAsync(
        string packagePath, CancellationToken cancellationToken)
    {
        var manifestPath = Path.Combine(packagePath, "manifest.yaml");
        if (!File.Exists(manifestPath))
        {
            return PackageResult<PackageManifest>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"package directory '{packagePath}' has no manifest.yaml");
        }

        ManifestDocument document;
        try
        {
            var yaml = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            document = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Deserialize<ManifestDocument>(yaml);
        }
        catch (YamlException e)
        {
            // YamlDotNet rejects unmatched properties by default, which is
            // the unknown-field rule (SQL.md, Manifest).
            return PackageResult<PackageManifest>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"invalid manifest '{manifestPath}': {e.Message}");
        }

        if (document is null)
        {
            return PackageResult<PackageManifest>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"manifest '{manifestPath}' is empty");
        }

        if (document.ApiVersion != "sqlpackage.v1")
        {
            return PackageResult<PackageManifest>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"manifest '{manifestPath}': apiVersion must be 'sqlpackage.v1'");
        }

        if (document.Kind != "Package")
        {
            return PackageResult<PackageManifest>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"manifest '{manifestPath}': kind must be 'Package'");
        }

        var metadata = document.Metadata;
        if (metadata is null || string.IsNullOrEmpty(metadata.Name) || !NamePattern.IsMatch(metadata.Name))
        {
            return PackageResult<PackageManifest>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"manifest '{manifestPath}': metadata.name must match [a-z0-9][a-z0-9-]{{0,63}}");
        }

        if (string.IsNullOrEmpty(metadata.Version) || !SemVerPattern.IsMatch(metadata.Version))
        {
            return PackageResult<PackageManifest>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"manifest '{manifestPath}': metadata.version must be a Semantic Versioning 2.0.0 version");
        }

        if (string.IsNullOrEmpty(metadata.Description) || metadata.Description.Length > 256)
        {
            return PackageResult<PackageManifest>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"manifest '{manifestPath}': metadata.description must be 1-256 characters");
        }

        var targets = document.Spec?.Targets;
        if (targets is null || string.IsNullOrWhiteSpace(targets.Core))
        {
            return PackageResult<PackageManifest>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"manifest '{manifestPath}': spec.targets.core is required");
        }

        if (targets.Database is null || !TargetDatabases.Contains(targets.Database))
        {
            return PackageResult<PackageManifest>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"manifest '{manifestPath}': spec.targets.database must be primary, analytics, or identity");
        }

        return PackageResult<PackageManifest>.Success(
            new PackageManifest(metadata.Name, metadata.Version, metadata.Description, targets.Core, targets.Database));
    }

    private static PackageResult<IReadOnlyList<MigrationFile>> DiscoverMigrations(string packagePath)
    {
        var migrationsDirectory = Path.Combine(packagePath, "migrations");
        if (!Directory.Exists(migrationsDirectory))
        {
            return PackageResult<IReadOnlyList<MigrationFile>>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"package directory '{packagePath}' has no migrations/ directory");
        }

        var files = Directory.GetFiles(migrationsDirectory);
        if (files.Length == 0)
        {
            return PackageResult<IReadOnlyList<MigrationFile>>.Failure(
                PackageErrorCode.E_MANIFEST_INVALID, $"package directory '{packagePath}' has no migration files");
        }

        var migrations = new List<MigrationFile>(files.Length);
        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var match = MigrationNamePattern.Match(fileName);
            if (!match.Success)
            {
                // SQL.md, Migrations: an invalid name is an error, not a skip.
                return PackageResult<IReadOnlyList<MigrationFile>>.Failure(
                    PackageErrorCode.E_MIGRATION_NAME_INVALID, $"migration file '{fileName}' does not match <NNN>_<slug>.sql");
            }

            migrations.Add(new MigrationFile(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), fileName, file));
        }

        var duplicateGroup = migrations.GroupBy(m => m.Number).FirstOrDefault(g => g.Count() > 1);
        if (duplicateGroup is not null)
        {
            var names = string.Join(", ", duplicateGroup.Select(m => m.FileName));
            return PackageResult<IReadOnlyList<MigrationFile>>.Failure(
                PackageErrorCode.E_MIGRATION_DUPLICATE_PREFIX, $"migrations {names} share the numeric prefix {duplicateGroup.Key}");
        }

        return PackageResult<IReadOnlyList<MigrationFile>>.Success(
            migrations.OrderBy(m => m.Number).ToList());
    }

    private sealed class ManifestDocument
    {
        public string? ApiVersion { get; set; }

        public string? Kind { get; set; }

        public ManifestMetadata? Metadata { get; set; }

        public ManifestSpec? Spec { get; set; }
    }

    private sealed class ManifestMetadata
    {
        public string? Name { get; set; }

        public string? Version { get; set; }

        public string? Description { get; set; }
    }

    private sealed class ManifestSpec
    {
        public ManifestTargets? Targets { get; set; }
    }

    private sealed class ManifestTargets
    {
        public string? Core { get; set; }

        public string? Database { get; set; }
    }
}
