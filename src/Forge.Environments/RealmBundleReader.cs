using System.Globalization;
using System.Text.RegularExpressions;
using Forge.Contracts;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Forge.Environments;

// Reads a realm bundle directory and validates it (DEPLOY.md, Environment
// Bundle, realm-named): apiVersion and kind, metadata.name, display name,
// spec.core against the configured core, the package references, and the
// realm id with its pass-through fields. Nothing here touches a database, so
// every refusal below happens before anything is written.
public static class RealmBundleReader
{
    // DEPLOY.md, Environment Bundle: metadata.name.
    private static readonly Regex NamePattern = new("^[a-z0-9][a-z0-9-]{0,31}$", RegexOptions.Compiled);

    public static async Task<RealmResult<RealmBundle>> ReadAsync(
        string bundlePath,
        string configuredCore,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(bundlePath))
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle directory '{bundlePath}' does not exist");
        }

        var manifestPath = Path.Combine(bundlePath, "manifest.yaml");
        if (!File.Exists(manifestPath))
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle directory '{bundlePath}' has no manifest.yaml");
        }

        RealmDocument document;
        try
        {
            var yaml = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            document = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Deserialize<RealmDocument>(yaml);
        }
        catch (YamlException e)
        {
            // YamlDotNet rejects unmatched properties by default, which is
            // the unknown-field rule (DEPLOY.md, Environment Bundle).
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"invalid bundle manifest '{manifestPath}': {e.Message}");
        }

        if (document is null)
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}' is empty");
        }

        if (document.ApiVersion != "realm.v1")
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}': apiVersion must be 'realm.v1'");
        }

        if (document.Kind != "Realm")
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}': kind must be 'Realm'");
        }

        var metadata = document.Metadata;
        if (metadata is null || string.IsNullOrEmpty(metadata.Name) || !NamePattern.IsMatch(metadata.Name))
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}': metadata.name must match [a-z0-9][a-z0-9-]{{0,31}}");
        }

        // The display name is written to the registration row's name column,
        // which the known schema limits to 32 characters.
        if (string.IsNullOrEmpty(metadata.DisplayName) || metadata.DisplayName.Length > 32)
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}': metadata.displayName must be 1-32 characters");
        }

        var spec = document.Spec;
        if (spec is null || string.IsNullOrWhiteSpace(spec.Core))
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}': spec.core is required");
        }

        if (!string.Equals(spec.Core, configuredCore, StringComparison.Ordinal))
        {
            // DEPLOY.md, Environment Bundle: exact string equality against
            // the configured core, never inferred from the value.
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_TARGET_MISMATCH,
                $"bundle '{metadata.Name}' targets core '{spec.Core}' but the configured core is '{configuredCore}'");
        }

        if (spec.Content?.Packages is null || spec.Content.Packages.Count == 0)
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}': spec.content.packages must list at least one package");
        }

        var packages = new List<RealmPackageReference>(spec.Content.Packages.Count);
        foreach (var package in spec.Content.Packages)
        {
            if (string.IsNullOrEmpty(package.Name) || !NamePattern.IsMatch(package.Name))
            {
                return RealmResult<RealmBundle>.Failure(
                    RealmErrorCode.E_BUNDLE_INVALID,
                    $"bundle manifest '{manifestPath}': package names must match [a-z0-9][a-z0-9-]{{0,31}}");
            }

            if (string.IsNullOrEmpty(package.Version))
            {
                return RealmResult<RealmBundle>.Failure(
                    RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}': package '{package.Name}' has no version");
            }

            packages.Add(new RealmPackageReference(package.Name, package.Version));
        }

        var realm = spec.Realm;
        if (realm is null)
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}': spec.realm is required");
        }

        // The known realmlist schema uses a tinyint unsigned id.
        if (realm.Id is null || realm.Id < 1 || realm.Id > 255)
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}': spec.realm.id must be an integer between 1 and 255");
        }

        if (realm.Icon is null || realm.Icon < 0 || realm.Icon > 255)
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}': spec.realm.icon must be an integer between 0 and 255");
        }

        if (realm.Timezone is null || realm.Timezone < 0 || realm.Timezone > 255)
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}': spec.realm.timezone must be an integer between 0 and 255");
        }

        if (realm.Population is null || realm.Population < 0)
        {
            return RealmResult<RealmBundle>.Failure(
                RealmErrorCode.E_BUNDLE_INVALID, $"bundle manifest '{manifestPath}': spec.realm.population must be a non-negative number");
        }

        return RealmResult<RealmBundle>.Success(
            new RealmBundle(
                metadata.Name,
                metadata.DisplayName,
                spec.Core,
                packages,
                realm.Id.Value,
                realm.Icon.Value,
                realm.Timezone.Value,
                realm.Population.Value));
    }

    private sealed class RealmDocument
    {
        public string? ApiVersion { get; set; }

        public string? Kind { get; set; }

        public RealmMetadata? Metadata { get; set; }

        public RealmSpec? Spec { get; set; }
    }

    private sealed class RealmMetadata
    {
        public string? Name { get; set; }

        public string? DisplayName { get; set; }
    }

    private sealed class RealmSpec
    {
        public string? Core { get; set; }

        public RealmContent? Content { get; set; }

        public RealmSettings? Realm { get; set; }
    }

    private sealed class RealmContent
    {
        public List<RealmPackageDocument>? Packages { get; set; }
    }

    private sealed class RealmPackageDocument
    {
        public string? Name { get; set; }

        public string? Version { get; set; }
    }

    private sealed class RealmSettings
    {
        public int? Id { get; set; }

        public int? Icon { get; set; }

        public int? Timezone { get; set; }

        public double? Population { get; set; }
    }
}
