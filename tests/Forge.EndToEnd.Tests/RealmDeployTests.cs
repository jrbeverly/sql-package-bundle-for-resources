using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using MySqlConnector;
using Xunit;

namespace Forge.EndToEnd.Tests;

[Trait("Category", "Integration")]
public class RealmDeployTests
{
    [Fact]
    public async Task DeployingABundleProducesDatabasesContentConfigRegistrationStateAndRunningContainer()
    {
        await InRealmHarnessAsync(async harness =>
        {
            var deploy = await RunForgeAsync("realm", "deploy", harness.BundleDirectory, "--config", harness.ConfigPath, "--state", harness.StateRoot);

            Assert.Equal(0, deploy.ExitCode);
            Assert.Contains($"deployed realm {harness.BundleName}", deploy.StdOut, StringComparison.Ordinal);

            // Both databases are provisioned, the character database empty.
            Assert.Equal(2, (await QueryRowsAsync(
                "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA " +
                $"WHERE SCHEMA_NAME IN ('world_{harness.BundleName}', 'character_{harness.BundleName}')")).Count);
            Assert.Empty(await QueryRowsAsync(
                $"SELECT table_name FROM information_schema.tables WHERE table_schema = 'character_{harness.BundleName}'"));

            // The package is installed and recorded in the world database,
            // layer-2 tracking tables included.
            Assert.Equal(
                ["customer-profiles", "1.0.0", "installed"],
                Assert.Single(await QueryRowsAsync(
                    $"SELECT `name`, `version`, `state` FROM `forge_installed_package`", database: $"world_{harness.BundleName}")));
            Assert.Equal(2, (await QueryRowsAsync(
                "SELECT * FROM `forge_applied_migration`", database: $"world_{harness.BundleName}")).Count);
            Assert.Equal(2, (await QueryRowsAsync(
                "SELECT * FROM `cp_customer_profile`", database: $"world_{harness.BundleName}")).Count);

            // Exactly one registration row carrying the realm's values.
            var registration = Assert.Single(await QueryRowsAsync(
                $"SELECT `id`, `name`, `address`, `port`, `icon`, `timezone` FROM `realmlist` WHERE `id` = {harness.RealmId}",
                database: harness.AuthDatabase));
            Assert.Equal(harness.DisplayName, registration[1]);
            Assert.Equal("127.0.0.1", registration[2]);
            var port = int.Parse(registration[3], CultureInfo.InvariantCulture);

            // The generated config differs from its template only in the five
            // realm-specific keys; every other line is byte-for-byte the
            // template's, comments included.
            var generatedPath = Path.Combine(harness.StateRoot, "realms", harness.BundleName, "mangosd.conf");
            Assert.True(File.Exists(generatedPath));
            var templateLines = File.ReadAllLines(CliPaths.RealmTemplate);
            var generatedLines = File.ReadAllLines(generatedPath);
            Assert.Equal(templateLines.Length, generatedLines.Length);
            for (var i = 0; i < templateLines.Length; i++)
            {
                if (!IsRealmSpecificKeyLine(templateLines[i]))
                {
                    Assert.Equal(templateLines[i], generatedLines[i]);
                }
            }

            Assert.Contains($"RealmID = {harness.RealmId}", generatedLines, StringComparer.Ordinal);
            Assert.Contains($"WorldServerPort = {port}", generatedLines, StringComparer.Ordinal);
            Assert.Contains($"LoginDatabaseInfo     = \"{harness.Host};{harness.DatabasePort};{harness.User};{harness.Password};{harness.AuthDatabase}\"", generatedLines, StringComparer.Ordinal);

            // Deployment state is recorded, and the placeholder container is
            // running with the generated config mounted.
            var state = await ReadStateAsync(harness.StateRoot, harness.BundleName);
            Assert.Equal("running", state.State);
            Assert.Equal(port, state.Port);
            Assert.Equal($"world_{harness.BundleName}", state.WorldDatabase);
            Assert.Equal($"character_{harness.BundleName}", state.CharacterDatabase);
            Assert.Equal("true", await DockerInspectRunningAsync($"realm-{harness.BundleName}"));
        });
    }

    [Fact]
    public async Task RedeployingTheSameBundleUpdatesTheRegistrationRow()
    {
        await InRealmHarnessAsync(async harness =>
        {
            var first = await RunForgeAsync("realm", "deploy", harness.BundleDirectory, "--config", harness.ConfigPath, "--state", harness.StateRoot);
            Assert.Equal(0, first.ExitCode);

            var second = await RunForgeAsync("realm", "deploy", harness.BundleDirectory, "--config", harness.ConfigPath, "--state", harness.StateRoot);

            Assert.Equal(0, second.ExitCode);
            Assert.Contains("already installed", second.StdOut, StringComparison.Ordinal);

            // The table never holds two rows for that realm id: the re-deploy
            // updated the existing one.
            Assert.Equal(
                [harness.DisplayName],
                (await QueryRowsAsync(
                    $"SELECT `name` FROM `realmlist` WHERE `id` = {harness.RealmId}", database: harness.AuthDatabase)).Select(r => r[0]));
            Assert.Equal(
                [1],
                (await QueryRowsAsync(
                    $"SELECT COUNT(*) FROM `realmlist` WHERE `id` = {harness.RealmId}", database: harness.AuthDatabase)).Select(r => int.Parse(r[0], CultureInfo.InvariantCulture)));

            Assert.Equal("true", await DockerInspectRunningAsync($"realm-{harness.BundleName}"));
        });
    }

    [Fact]
    public async Task UnresolvablePackageIsReportedAndCreatesNoDatabases()
    {
        await InRealmHarnessAsync(async harness =>
        {
            var broken = await WriteBundleAsync(
                harness.BundleName, harness.RealmId, [("extension-framework", "2.1.0")], harness.BundleDirectory);

            var deploy = await RunForgeAsync("realm", "deploy", broken, "--config", harness.ConfigPath, "--state", harness.StateRoot);

            Assert.Equal(14, deploy.ExitCode);
            Assert.Contains("E_PACKAGE_UNRESOLVED", deploy.StdErr, StringComparison.Ordinal);
            Assert.Empty(await QueryRowsAsync(
                "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA " +
                $"WHERE SCHEMA_NAME IN ('world_{harness.BundleName}', 'character_{harness.BundleName}')"));
        });
    }

    [Fact]
    public async Task UninstallablePackageIsReportedAndCreatesNoDatabases()
    {
        await InRealmHarnessAsync(async harness =>
        {
            var broken = await WriteBundleAsync(
                harness.BundleName, harness.RealmId, [("customer-profiles", "1.0.0")], harness.BundleDirectory,
                migration: "THIS IS NOT SQL;");

            var deploy = await RunForgeAsync("realm", "deploy", broken, "--config", harness.ConfigPath, "--state", harness.StateRoot);

            Assert.Equal(15, deploy.ExitCode);
            Assert.Contains("E_MIGRATION_FAILED", deploy.StdErr, StringComparison.Ordinal);
            Assert.Empty(await QueryRowsAsync(
                "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA " +
                $"WHERE SCHEMA_NAME IN ('world_{harness.BundleName}', 'character_{harness.BundleName}')"));
        });
    }

    [Fact]
    public async Task TemplateMissingAKeyIsRefusedAndNothingIsWritten()
    {
        await InRealmHarnessAsync(async harness =>
        {
            var template = Path.Combine(Path.GetTempPath(), $"forge_test_template_{Guid.NewGuid():N}.conf");
            await File.WriteAllTextAsync(template, """
                RealmID = 1
                LoginDatabaseInfo = "127.0.0.1;3306;mangos;mangos;realmd"
                WorldDatabaseInfo = "127.0.0.1;3306;mangos;mangos;mangos"
                CharacterDatabaseInfo = "127.0.0.1;3306;mangos;mangos;characters"
                """);
            var configYaml = await File.ReadAllTextAsync(harness.ConfigPath);
            await File.WriteAllTextAsync(harness.ConfigPath, configYaml.Replace(CliPaths.RealmTemplate, template, StringComparison.Ordinal));

            var deploy = await RunForgeAsync("realm", "deploy", harness.BundleDirectory, "--config", harness.ConfigPath, "--state", harness.StateRoot);

            Assert.Equal(3, deploy.ExitCode);
            Assert.Contains("E_CONFIG_KEY_MISSING", deploy.StdErr, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(harness.StateRoot, "realms", harness.BundleName)));
            Assert.Empty(await QueryRowsAsync(
                "SELECT SCHEMA_NAME FROM information_schema.SCHEMATA " +
                $"WHERE SCHEMA_NAME IN ('world_{harness.BundleName}', 'character_{harness.BundleName}')"));
        });
    }

    [Fact]
    public async Task TwoDifferentBundlesDeployWithDifferentPorts()
    {
        var authDatabase = await CreateAuthDatabaseAsync();
        var stateRoot = Path.Combine(Path.GetTempPath(), $"forge_test_state_{Guid.NewGuid():N}");
        var basePort = 32000 + RandomPortOffset();
        try
        {
            var firstName = $"forge{ShortGuid()}";
            var secondName = $"forge{ShortGuid()}";
            var firstBundle = await WriteBundleAsync(firstName, 11, [("customer-profiles", "1.0.0")], null);
            var secondBundle = await WriteBundleAsync(secondName, 12, [("customer-profiles", "1.0.0")], null);
            var configPath = await WriteConfigAsync(authDatabase, CliPaths.RealmTemplate, basePort);

            var first = await RunForgeAsync("realm", "deploy", firstBundle, "--config", configPath, "--state", stateRoot);
            var second = await RunForgeAsync("realm", "deploy", secondBundle, "--config", configPath, "--state", stateRoot);

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);

            var firstState = await ReadStateAsync(stateRoot, firstName);
            var secondState = await ReadStateAsync(stateRoot, secondName);
            Assert.NotEqual(firstState.Port, secondState.Port);
            Assert.True(firstState.Port >= basePort);
            Assert.True(secondState.Port >= basePort);

            // Both realms are registered against the one authentication
            // database, each with its own port.
            Assert.Equal(2, (await QueryRowsAsync("SELECT * FROM `realmlist`", database: authDatabase)).Count);
            Assert.Equal("true", await DockerInspectRunningAsync($"realm-{firstName}"));
            Assert.Equal("true", await DockerInspectRunningAsync($"realm-{secondName}"));

            await DockerComposeDownAsync(stateRoot, firstName);
            await DockerComposeDownAsync(stateRoot, secondName);
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS `world_{firstName}`");
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS `character_{firstName}`");
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS `world_{secondName}`");
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS `character_{secondName}`");
        }
        finally
        {
            Directory.Delete(stateRoot, recursive: true);
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS `{authDatabase}`");
        }
    }

    [Fact]
    public async Task DeployingASecondRealmSharesOnlyTheAuthenticationDatabase()
    {
        var authDatabase = await CreateAuthDatabaseAsync();
        var stateRoot = Path.Combine(Path.GetTempPath(), $"forge_test_state_{Guid.NewGuid():N}");
        var basePort = 32000 + RandomPortOffset();
        try
        {
            var configPath = await WriteConfigAsync(authDatabase, CliPaths.RealmTemplate, basePort);

            // The walk deploys the two fixture bundles one after the other,
            // each referencing the same customer-profiles package.
            var first = await RunForgeAsync("realm", "deploy", CliPaths.RealmSearingGorge, "--config", configPath, "--state", stateRoot);
            var second = await RunForgeAsync("realm", "deploy", CliPaths.RealmBlackrockDepths, "--config", configPath, "--state", stateRoot);

            Assert.Equal(0, first.ExitCode);
            Assert.Equal(0, second.ExitCode);

            // Both realms are registered in the one shared authentication
            // database, each row carrying its own id, name, and port.
            var registrations = await QueryRowsAsync(
                "SELECT `id`, `name`, `address`, `port` FROM `realmlist` ORDER BY `id`", database: authDatabase);
            Assert.Equal(2, registrations.Count);
            Assert.Equal("1", registrations[0][0]);
            Assert.Equal("2", registrations[1][0]);
            Assert.Equal("Searing Gorge", registrations[0][1]);
            Assert.Equal("Blackrock Depths", registrations[1][1]);
            Assert.Equal("127.0.0.1", registrations[0][2]);
            Assert.Equal("127.0.0.1", registrations[1][2]);
            Assert.NotEqual(registrations[0][3], registrations[1][3]);

            var firstState = await ReadStateAsync(stateRoot, "searing-gorge");
            var secondState = await ReadStateAsync(stateRoot, "blackrock-depths");
            Assert.NotEqual(firstState.Port, secondState.Port);
            Assert.True(firstState.Port >= basePort);
            Assert.True(secondState.Port >= basePort);

            // Each realm's world database holds the package's content.
            Assert.Equal(2, (await QueryRowsAsync(
                "SELECT * FROM `cp_customer_profile`", database: "world_searing-gorge")).Count);
            Assert.Equal(2, (await QueryRowsAsync(
                "SELECT * FROM `cp_customer_profile`", database: "world_blackrock-depths")).Count);

            // The same package is recorded as installed in independent
            // tracking tables, one set per realm's world database, each
            // describing only its own realm's install.
            foreach (var worldDatabase in new[] { "world_searing-gorge", "world_blackrock-depths" })
            {
                Assert.Equal(
                    ["customer-profiles", "1.0.0", "installed"],
                    Assert.Single(await QueryRowsAsync(
                        "SELECT `name`, `version`, `state` FROM `forge_installed_package`", database: worldDatabase)));
                Assert.Equal(2, (await QueryRowsAsync(
                    "SELECT * FROM `forge_applied_migration`", database: worldDatabase)).Count);
            }

            // Content applied to one realm's world database afterward is
            // absent from the other's.
            await ExecuteAdminAsync("CREATE TABLE `world_searing-gorge`.`walk_marker` (`value` VARCHAR(32) NOT NULL)");
            await ExecuteAdminAsync("INSERT INTO `world_searing-gorge`.`walk_marker` VALUES ('searing-gorge-only')");
            await ExecuteAdminAsync("CREATE TABLE `world_blackrock-depths`.`walk_marker` (`value` VARCHAR(32) NOT NULL)");
            await ExecuteAdminAsync("INSERT INTO `world_blackrock-depths`.`walk_marker` VALUES ('blackrock-depths-only')");
            Assert.Equal(
                ["searing-gorge-only"],
                (await QueryRowsAsync("SELECT `value` FROM `walk_marker`", database: "world_searing-gorge")).Select(r => r[0]));
            Assert.Equal(
                ["blackrock-depths-only"],
                (await QueryRowsAsync("SELECT `value` FROM `walk_marker`", database: "world_blackrock-depths")).Select(r => r[0]));

            // Stopping one realm's container leaves the other's running, and
            // the recorded state follows the container.
            var stop = await RunForgeAsync("realm", "stop", "searing-gorge", "--state", stateRoot);
            Assert.Equal(0, stop.ExitCode);
            Assert.Contains("stopped realm searing-gorge", stop.StdOut, StringComparison.Ordinal);
            Assert.Equal("false", await DockerInspectRunningAsync("realm-searing-gorge"));
            Assert.Equal("true", await DockerInspectRunningAsync("realm-blackrock-depths"));
            Assert.Equal("stopped", (await ReadStateAsync(stateRoot, "searing-gorge")).State);
            Assert.Equal("running", (await ReadStateAsync(stateRoot, "blackrock-depths")).State);

            // Stop is idempotent, and start brings the realm back without
            // disturbing the other.
            Assert.Equal(0, (await RunForgeAsync("realm", "stop", "searing-gorge", "--state", stateRoot)).ExitCode);
            var start = await RunForgeAsync("realm", "start", "searing-gorge", "--state", stateRoot);
            Assert.Equal(0, start.ExitCode);
            Assert.Contains("started realm searing-gorge", start.StdOut, StringComparison.Ordinal);
            Assert.Equal("true", await DockerInspectRunningAsync("realm-searing-gorge"));
            Assert.Equal("true", await DockerInspectRunningAsync("realm-blackrock-depths"));
            Assert.Equal("running", (await ReadStateAsync(stateRoot, "searing-gorge")).State);

            await DockerComposeDownAsync(stateRoot, "searing-gorge");
            await DockerComposeDownAsync(stateRoot, "blackrock-depths");
            await ExecuteAdminAsync("DROP DATABASE IF EXISTS `world_searing-gorge`");
            await ExecuteAdminAsync("DROP DATABASE IF EXISTS `character_searing-gorge`");
            await ExecuteAdminAsync("DROP DATABASE IF EXISTS `world_blackrock-depths`");
            await ExecuteAdminAsync("DROP DATABASE IF EXISTS `character_blackrock-depths`");
        }
        finally
        {
            if (Directory.Exists(stateRoot))
            {
                Directory.Delete(stateRoot, recursive: true);
            }

            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS `{authDatabase}`");
        }
    }

    [Fact]
    public async Task StoppingOrStartingAnUndeployedRealmIsRefused()
    {
        var stateRoot = Path.Combine(Path.GetTempPath(), $"forge_test_state_{Guid.NewGuid():N}");
        try
        {
            var stop = await RunForgeAsync("realm", "stop", "never-deployed", "--state", stateRoot);
            Assert.Equal(13, stop.ExitCode);
            Assert.Contains("E_REALM_NOT_DEPLOYED", stop.StdErr, StringComparison.Ordinal);

            var start = await RunForgeAsync("realm", "start", "never-deployed", "--state", stateRoot);
            Assert.Equal(13, start.ExitCode);
            Assert.Contains("E_REALM_NOT_DEPLOYED", start.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(stateRoot))
            {
                Directory.Delete(stateRoot, recursive: true);
            }
        }
    }

    // ── harness ──────────────────────────────────────────────────────────

    private sealed record RealmHarness(
        string AuthDatabase,
        string StateRoot,
        string BundleName,
        string DisplayName,
        int RealmId,
        string BundleDirectory,
        string ConfigPath,
        string Host,
        uint DatabasePort,
        string User,
        string Password);

    private static async Task InRealmHarnessAsync(Func<RealmHarness, Task> test)
    {
        var authDatabase = await CreateAuthDatabaseAsync();
        var stateRoot = Path.Combine(Path.GetTempPath(), $"forge_test_state_{Guid.NewGuid():N}");
        var bundleName = $"forge{ShortGuid()}";
        var bundleDirectory = Path.Combine(Path.GetTempPath(), $"forge_test_bundle_{Guid.NewGuid():N}");
        var builder = new MySqlConnectionStringBuilder(TestConnectionString());
        var basePort = 32000 + RandomPortOffset();
        var configPath = await WriteConfigAsync(authDatabase, CliPaths.RealmTemplate, basePort);
        var harness = new RealmHarness(
            authDatabase, stateRoot, bundleName, bundleName, 1 + RandomPortOffset() % 250,
            bundleDirectory, configPath, builder.Server, builder.Port, builder.UserID, builder.Password);
        try
        {
            await WriteBundleAsync(bundleName, harness.RealmId, [("customer-profiles", "1.0.0")], bundleDirectory);
            await test(harness);
        }
        finally
        {
            await DockerComposeDownAsync(stateRoot, bundleName);
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS `world_{bundleName}`");
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS `character_{bundleName}`");
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS `{authDatabase}`");
            if (Directory.Exists(stateRoot))
            {
                Directory.Delete(stateRoot, recursive: true);
            }

            if (Directory.Exists(bundleDirectory))
            {
                Directory.Delete(bundleDirectory, recursive: true);
            }
        }
    }

    private static async Task<string> CreateAuthDatabaseAsync()
    {
        var database = $"forge_test_auth_{Guid.NewGuid():N}";
        await ExecuteAdminAsync($"CREATE DATABASE `{database}`");
        await using var connection = new MySqlConnection(TestConnectionString());
        await connection.OpenAsync();
        await connection.ChangeDatabaseAsync(database);
        await using var command = new MySqlCommand(await File.ReadAllTextAsync(CliPaths.RealmAuthSchema), connection);
        await command.ExecuteNonQueryAsync();
        return database;
    }

    private static async Task<string> WriteConfigAsync(string authDatabase, string template, int basePort)
    {
        var builder = new MySqlConnectionStringBuilder(TestConnectionString());
        var path = Path.Combine(Path.GetTempPath(), $"forge_test_cfg_{Guid.NewGuid():N}.yaml");
        var yaml = $"""
            apiVersion: forge.v1
            kind: Config

            spec:
              core: example-app
              database:
                host: {builder.Server}
                port: {builder.Port}
                user: {builder.UserID}
                password: {builder.Password}
                primary: primary
                identity: {authDatabase}
              realm:
                configTemplate: {template}
                image: busybox:latest
                basePort: {basePort}
                advertisedAddress: 127.0.0.1
            """;
        await File.WriteAllTextAsync(path, yaml);
        return path;
    }

    private static async Task<string> WriteBundleAsync(
        string name,
        int realmId,
        IReadOnlyList<(string Package, string Version)> packages,
        string? directory,
        string? migration = null)
    {
        directory ??= Path.Combine(Path.GetTempPath(), $"forge_test_bundle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "packages", "customer-profiles", "migrations"));
        await File.WriteAllTextAsync(Path.Combine(directory, "packages", "customer-profiles", "manifest.yaml"), """
            apiVersion: sqlpackage.v1
            kind: Package

            metadata:
              name: customer-profiles
              version: 1.0.0
              description: Customer profile resources

            spec:
              targets:
                core: example-app
                database: primary
            """);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "packages", "customer-profiles", "migrations", "001_customer_profiles.sql"),
            migration ?? """
                CREATE TABLE `cp_customer_profile` (`entry` INT UNSIGNED NOT NULL, PRIMARY KEY (`entry`));
                INSERT INTO `cp_customer_profile` (`entry`) VALUES (400001), (400002);
                """);
        await File.WriteAllTextAsync(
            Path.Combine(directory, "packages", "customer-profiles", "migrations", "002_profile_contacts.sql"),
            """
            CREATE TABLE `cp_profile_contact` (`entry` INT UNSIGNED NOT NULL, PRIMARY KEY (`entry`));
            INSERT INTO `cp_profile_contact` (`entry`) VALUES (400101), (400102);
            """);

        var references = string.Join("\n", packages.Select(p => $"      - name: {p.Package}\n        version: {p.Version}"));
        var manifest = $"""
            apiVersion: realm.v1
            kind: Realm

            metadata:
              name: {name}
              displayName: {name}

            spec:
              core: example-app
              content:
                packages:
            {references}
              realm:
                id: {realmId}
                icon: 0
                timezone: 0
                population: 0
            """;
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.yaml"), manifest);
        return directory;
    }

    private static bool IsRealmSpecificKeyLine(string line) =>
        line.StartsWith("RealmID ", StringComparison.Ordinal)
        || line.StartsWith("LoginDatabaseInfo ", StringComparison.Ordinal)
        || line.StartsWith("WorldDatabaseInfo ", StringComparison.Ordinal)
        || line.StartsWith("CharacterDatabaseInfo ", StringComparison.Ordinal)
        || line.StartsWith("WorldServerPort ", StringComparison.Ordinal);

    private static async Task<(string Name, string State, int Port, string WorldDatabase, string CharacterDatabase)> ReadStateAsync(
        string stateRoot, string name)
    {
        var json = await File.ReadAllTextAsync(Path.Combine(stateRoot, "realms", name, "state.json"));
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return (
            root.GetProperty("name").GetString()!,
            root.GetProperty("state").GetString()!,
            root.GetProperty("port").GetInt32(),
            root.GetProperty("worldDatabase").GetString()!,
            root.GetProperty("characterDatabase").GetString()!);
    }

    private static async Task<string> DockerInspectRunningAsync(string container)
    {
        var inspect = await RunDockerAsync("docker", ["inspect", "-f", "{{.State.Running}}", container]);
        return inspect.StdOut.Trim();
    }

    private static async Task DockerComposeDownAsync(string stateRoot, string name)
    {
        var composePath = Path.Combine(stateRoot, "realms", name, "compose.yaml");
        if (File.Exists(composePath))
        {
            await RunDockerAsync("docker", ["compose", "-f", composePath, "down"]);
        }

        await RunDockerAsync("docker", ["rm", "-f", $"realm-{name}"]);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunForgeAsync(params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = CliPaths.ForgeBinary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"could not start {CliPaths.ForgeBinary}; run the build first (make validate)");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(12));
        return (process.ExitCode, stdout, stderr);
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunDockerAsync(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"could not start {fileName}");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(12));
        return (process.ExitCode, stdout, stderr);
    }

    private static string TestConnectionString()
    {
        var connectionString = Environment.GetEnvironmentVariable("FORGE_TEST_MYSQL");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "FORGE_TEST_MYSQL is not set. Integration tests need a running MySQL: " +
                "start it with `make db-up`, then set FORGE_TEST_MYSQL to " +
                "\"Server=127.0.0.1;Port=3306;User ID=forge;Password=forge-test;Database=primary\".");
        }

        return connectionString;
    }

    private static async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new MySqlConnection(TestConnectionString());
        await connection.OpenAsync();
        await using var command = new MySqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string[]>> QueryRowsAsync(string sql, string? database = null)
    {
        await using var connection = new MySqlConnection(TestConnectionString());
        await connection.OpenAsync();
        if (database is not null)
        {
            await connection.ChangeDatabaseAsync(database);
        }

        await using var command = new MySqlCommand(sql, connection);
        var rows = new List<string[]>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var values = new string[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                values[i] = Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? string.Empty;
            }

            rows.Add(values);
        }

        return rows;
    }

    private static string ShortGuid() => Guid.NewGuid().ToString("N")[..8];

    private static int RandomPortOffset() => Math.Abs(BitConverter.ToInt32(Guid.NewGuid().ToByteArray(), 0)) % 8000;
}
