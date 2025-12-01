using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Forge.Cli;

// Reads the config file subset package commands need (TECHNICAL.md,
// Configuration). Sections outside that subset are accepted but unused;
// unknown fields are rejected, not ignored.
internal static class ConfigReader
{
    public static async Task<ForgeConfig> ReadAsync(
        string path, string? environmentPassword, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw new ConfigException($"config file '{path}' does not exist");
        }

        ConfigDocument document;
        try
        {
            var yaml = await File.ReadAllTextAsync(path, cancellationToken);
            document = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build()
                .Deserialize<ConfigDocument>(yaml);
        }
        catch (YamlException e)
        {
            throw new ConfigException($"config file '{path}' is invalid: {e.Message}");
        }

        if (document is null || document.ApiVersion != "forge.v1")
        {
            throw new ConfigException($"config file '{path}': apiVersion must be 'forge.v1'");
        }

        if (document.Kind != "Config")
        {
            throw new ConfigException($"config file '{path}': kind must be 'Config'");
        }

        var spec = document.Spec;
        if (spec is null || string.IsNullOrWhiteSpace(spec.Core))
        {
            throw new ConfigException($"config file '{path}': spec.core is required");
        }

        var database = spec.Database;
        if (database is null || string.IsNullOrWhiteSpace(database.Host))
        {
            throw new ConfigException($"config file '{path}': spec.database.host is required");
        }

        if (string.IsNullOrWhiteSpace(database.User))
        {
            throw new ConfigException($"config file '{path}': spec.database.user is required");
        }

        if (string.IsNullOrWhiteSpace(database.Primary))
        {
            throw new ConfigException($"config file '{path}': spec.database.primary is required");
        }

        var password = environmentPassword ?? database.Password;
        if (string.IsNullOrEmpty(password))
        {
            throw new ConfigException("no database password: set it in the config file or in FORGE_DB_PASSWORD");
        }

        return new ForgeConfig(
            spec.Core,
            database.Host,
            database.Port ?? 3306u,
            database.User,
            password,
            database.Primary,
            database.Analytics,
            database.Identity,
            spec.Realm?.ConfigTemplate,
            spec.Realm?.Image,
            spec.Realm?.BasePort,
            spec.Realm?.AdvertisedAddress);
    }

    private sealed class ConfigDocument
    {
        public string? ApiVersion { get; set; }

        public string? Kind { get; set; }

        public ConfigSpec? Spec { get; set; }

        // The environment and catalog sections exist in the documented config but
        // are unused by package commands; they are accepted, not validated.
        public Dictionary<string, object>? Environment { get; set; }

        public Dictionary<string, object>? Catalog { get; set; }
    }

    private sealed class ConfigSpec
    {
        public string? Core { get; set; }

        public ConfigDatabase? Database { get; set; }

        public ConfigRealm? Realm { get; set; }
    }

    private sealed class ConfigRealm
    {
        public string? ConfigTemplate { get; set; }

        public string? Image { get; set; }

        public int? BasePort { get; set; }

        public string? AdvertisedAddress { get; set; }
    }

    private sealed class ConfigDatabase
    {
        public string? Host { get; set; }

        public uint? Port { get; set; }

        public string? User { get; set; }

        public string? Password { get; set; }

        public string? Primary { get; set; }

        public string? Analytics { get; set; }

        public string? Identity { get; set; }
    }
}
