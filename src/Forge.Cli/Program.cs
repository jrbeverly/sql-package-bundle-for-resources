using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Forge.Contracts;
using Forge.Database;
using Forge.Environments;
using Forge.Packaging;

namespace Forge.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("forge: catalog, package, and environment operations for application content.");
        var configOption = new Option<FileInfo?>("--config", "Path to the forge config file (default: $FORGE_CONFIG or ~/.config/forge/config.yaml).");
        rootCommand.AddGlobalOption(configOption);
        var stateOption = new Option<DirectoryInfo?>("--state", "State root for realm deployments (default: $FORGE_STATE or ~/.local/share/forge).");
        rootCommand.AddGlobalOption(stateOption);

        var packageCommand = new Command("package", "SQL package operations (SQL.md).");
        rootCommand.Add(packageCommand);

        var pathArgument = new Argument<FileInfo>("path", "Path to a local package directory.");
        var installCommand = new Command("install", "Install a package from a local directory.");
        installCommand.AddArgument(pathArgument);
        packageCommand.Add(installCommand);

        var upgradeCommand = new Command("upgrade", "Upgrade an installed package from a local directory.");
        upgradeCommand.AddArgument(pathArgument);
        packageCommand.Add(upgradeCommand);

        var listCommand = new Command("list", "List installed packages from the tracking tables.");
        packageCommand.Add(listCommand);

        var realmCommand = new Command("realm", "Realm deployment operations (DEPLOY.md).");
        rootCommand.Add(realmCommand);

        var bundleArgument = new Argument<DirectoryInfo>("bundle", "Path to a realm bundle directory.");
        var deployCommand = new Command("deploy", "Deploy a realm from a bundle directory.");
        deployCommand.AddArgument(bundleArgument);
        realmCommand.Add(deployCommand);

        var nameArgument = new Argument<string>("name", "The deployed realm's metadata.name.");
        var stopCommand = new Command("stop", "Stop a deployed realm's container (DEPLOY.md, Start And Stop).");
        stopCommand.AddArgument(nameArgument);
        realmCommand.Add(stopCommand);

        var startCommand = new Command("start", "Start a deployed realm's container (DEPLOY.md, Start And Stop).");
        startCommand.AddArgument(nameArgument);
        realmCommand.Add(startCommand);

        installCommand.SetHandler((Func<InvocationContext, Task>)(async context =>
        {
            context.ExitCode = await InstallAsync(context.ParseResult, configOption, pathArgument);
        }));

        upgradeCommand.SetHandler((Func<InvocationContext, Task>)(async context =>
        {
            context.ExitCode = await UpgradeAsync(context.ParseResult, configOption, pathArgument);
        }));

        listCommand.SetHandler((Func<InvocationContext, Task>)(async context =>
        {
            context.ExitCode = await ListAsync(context.ParseResult, configOption);
        }));

        deployCommand.SetHandler((Func<InvocationContext, Task>)(async context =>
        {
            context.ExitCode = await DeployAsync(context.ParseResult, configOption, stateOption, bundleArgument);
        }));

        stopCommand.SetHandler((Func<InvocationContext, Task>)(async context =>
        {
            context.ExitCode = await StopAsync(context.ParseResult, stateOption, nameArgument);
        }));

        startCommand.SetHandler((Func<InvocationContext, Task>)(async context =>
        {
            context.ExitCode = await StartAsync(context.ParseResult, stateOption, nameArgument);
        }));

        try
        {
            return await rootCommand.InvokeAsync(args);
        }
        catch (ConfigException e)
        {
            Console.Error.WriteLine($"configuration error: {e.Message}");
            return 3;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"internal error: {e}");
            return 1;
        }
    }

    private static async Task<int> InstallAsync(
        ParseResult parseResult, Option<FileInfo?> configOption, Argument<FileInfo> pathArgument)
    {
        var config = await ConfigReader.ReadAsync(
            ResolveConfigPath(parseResult.GetValueForOption(configOption)),
            Environment.GetEnvironmentVariable("FORGE_DB_PASSWORD"));

        var packagePath = parseResult.GetValueForArgument(pathArgument).FullName;
        var package = await PackageReader.ReadAsync(packagePath, config.Core);
        if (package.ErrorCode is not null)
        {
            return WriteFailure(package.ErrorCode.Value, package.Message!);
        }

        var prepared = package.Value!;
        var manifest = prepared.Manifest;
        var database = DatabaseNameFor(config, manifest.TargetDatabase);
        var connectionString = DatabaseConnection.For(config.Host, config.Port, config.User, config.Password, database);

        Console.WriteLine($"installing {manifest.Name} {manifest.Version} ({prepared.Migrations.Count} migrations)");
        var installed = await PackageInstaller.InstallAsync(connectionString, database, prepared, Environment.UserName);
        if (installed.ErrorCode is not null)
        {
            return WriteFailure(installed.ErrorCode.Value, installed.Message!);
        }

        Console.WriteLine($"installed {installed.Value!.Name} {installed.Value.Version}");
        return 0;
    }

    private static async Task<int> UpgradeAsync(
        ParseResult parseResult, Option<FileInfo?> configOption, Argument<FileInfo> pathArgument)
    {
        var config = await ConfigReader.ReadAsync(
            ResolveConfigPath(parseResult.GetValueForOption(configOption)),
            Environment.GetEnvironmentVariable("FORGE_DB_PASSWORD"));

        var packagePath = parseResult.GetValueForArgument(pathArgument).FullName;
        var package = await PackageReader.ReadAsync(packagePath, config.Core);
        if (package.ErrorCode is not null)
        {
            return WriteFailure(package.ErrorCode.Value, package.Message!);
        }

        var prepared = package.Value!;
        var manifest = prepared.Manifest;
        var database = DatabaseNameFor(config, manifest.TargetDatabase);
        var connectionString = DatabaseConnection.For(config.Host, config.Port, config.User, config.Password, database);

        Console.WriteLine($"upgrading {manifest.Name} {manifest.Version} ({prepared.Migrations.Count} migrations)");
        var upgraded = await PackageUpgrader.UpgradeAsync(connectionString, database, prepared);
        if (upgraded.ErrorCode is not null)
        {
            return WriteFailure(upgraded.ErrorCode.Value, upgraded.Message!);
        }

        if (upgraded.Value!.VersionChanged)
        {
            Console.WriteLine($"upgraded {manifest.Name} to {manifest.Version}");
        }
        else
        {
            Console.WriteLine($"package '{manifest.Name}' is already at version {manifest.Version}; nothing to do");
        }

        return 0;
    }

    private static async Task<int> ListAsync(ParseResult parseResult, Option<FileInfo?> configOption)
    {
        var config = await ConfigReader.ReadAsync(
            ResolveConfigPath(parseResult.GetValueForOption(configOption)),
            Environment.GetEnvironmentVariable("FORGE_DB_PASSWORD"));

        var connectionString = DatabaseConnection.For(config.Host, config.Port, config.User, config.Password, config.Primary);
        var packages = await PackageLister.ListAsync(connectionString, config.Primary);
        if (packages.ErrorCode is not null)
        {
            return WriteFailure(packages.ErrorCode.Value, packages.Message!);
        }

        foreach (var package in packages.Value!)
        {
            Console.WriteLine($"{package.Name} {package.Version} {package.State}");
        }

        return 0;
    }

    private static async Task<int> DeployAsync(
        ParseResult parseResult, Option<FileInfo?> configOption, Option<DirectoryInfo?> stateOption, Argument<DirectoryInfo> bundleArgument)
    {
        var config = await ConfigReader.ReadAsync(
            ResolveConfigPath(parseResult.GetValueForOption(configOption)),
            Environment.GetEnvironmentVariable("FORGE_DB_PASSWORD"));

        if (string.IsNullOrWhiteSpace(config.Identity))
        {
            throw new ConfigException("realm deploy needs spec.database.identity: the shared authentication database name");
        }

        if (string.IsNullOrWhiteSpace(config.RealmTemplate))
        {
            throw new ConfigException("realm deploy needs spec.realm.configTemplate: the core's config template");
        }

        if (!File.Exists(config.RealmTemplate))
        {
            throw new ConfigException($"config template '{config.RealmTemplate}' does not exist");
        }

        if (string.IsNullOrWhiteSpace(config.RealmImage))
        {
            throw new ConfigException("realm deploy needs spec.realm.image: the placeholder container image");
        }

        if (string.IsNullOrWhiteSpace(config.RealmAdvertisedAddress))
        {
            // The address a client connects to comes from configuration,
            // never a guess from a local interface (DEPLOY.md, Registration).
            throw new ConfigException("realm deploy needs spec.realm.advertisedAddress: the address clients use to reach the realm");
        }

        var bundlePath = parseResult.GetValueForArgument(bundleArgument).FullName;
        var deployConfig = new DeployConfig(
            config.Core,
            config.Host,
            config.Port,
            config.User,
            config.Password,
            config.Identity,
            ResolveStatePath(parseResult.GetValueForOption(stateOption)),
            config.RealmTemplate,
            config.RealmImage,
            config.RealmBasePort ?? 8085,
            config.RealmAdvertisedAddress);

        Console.WriteLine($"deploying realm from {bundlePath}");
        var deployed = await RealmDeployer.DeployAsync(bundlePath, deployConfig);
        if (deployed.ErrorCode is not null)
        {
            return WriteFailure(deployed.ErrorCode.Value, deployed.Message!);
        }

        foreach (var note in deployed.Value!.Notes)
        {
            Console.WriteLine(note);
        }

        var state = deployed.Value.State;
        Console.WriteLine($"deployed realm {state.Name} (realm id {state.RealmId})");
        Console.WriteLine($"world database {state.WorldDatabase}, character database {state.CharacterDatabase}");
        Console.WriteLine($"port {state.Port}, container {state.Container}, state {state.State}");
        return 0;
    }

    private static async Task<int> StopAsync(
        ParseResult parseResult, Option<DirectoryInfo?> stateOption, Argument<string> nameArgument)
    {
        var name = parseResult.GetValueForArgument(nameArgument);
        var stopped = await RealmLifecycle.StopAsync(ResolveStatePath(parseResult.GetValueForOption(stateOption)), name);
        if (stopped.ErrorCode is not null)
        {
            return WriteFailure(stopped.ErrorCode.Value, stopped.Message!);
        }

        Console.WriteLine($"stopped realm {name} (container {stopped.Value!.Container})");
        return 0;
    }

    private static async Task<int> StartAsync(
        ParseResult parseResult, Option<DirectoryInfo?> stateOption, Argument<string> nameArgument)
    {
        var name = parseResult.GetValueForArgument(nameArgument);
        var started = await RealmLifecycle.StartAsync(ResolveStatePath(parseResult.GetValueForOption(stateOption)), name);
        if (started.ErrorCode is not null)
        {
            return WriteFailure(started.ErrorCode.Value, started.Message!);
        }

        Console.WriteLine($"started realm {name} (container {started.Value!.Container})");
        return 0;
    }

    private static string ResolveConfigPath(FileInfo? fromOption)
    {
        return fromOption?.FullName
            ?? Environment.GetEnvironmentVariable("FORGE_CONFIG")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "forge", "config.yaml");
    }

    private static string ResolveStatePath(DirectoryInfo? fromOption)
    {
        return fromOption?.FullName
            ?? Environment.GetEnvironmentVariable("FORGE_STATE")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "forge");
    }

    // The manifest's spec.targets.database selects which configured database
    // name the migrations apply to (TECHNICAL.md, CLI Surface).
    private static string DatabaseNameFor(ForgeConfig config, string manifestDatabase) => manifestDatabase switch
    {
        "primary" => config.Primary,
        "analytics" => config.Analytics
            ?? throw new ConfigException("the manifest targets 'analytics' but the config has no spec.database.analytics"),
        "identity" => config.Identity
            ?? throw new ConfigException("the manifest targets 'identity' but the config has no spec.database.identity"),
        _ => throw new ConfigException($"the manifest targets unknown database '{manifestDatabase}'"),
    };

    private static int WriteFailure(PackageErrorCode errorCode, string message)
    {
        Console.Error.WriteLine($"{errorCode}: {message}");
        return ExitCodeFor(errorCode);
    }

    private static int WriteFailure(RealmErrorCode errorCode, string message)
    {
        Console.Error.WriteLine($"{errorCode}: {message}");
        return ExitCodeFor(errorCode);
    }

    // TECHNICAL.md, Exit Codes: the mapping from SQL.md error codes.
    private static int ExitCodeFor(PackageErrorCode errorCode) => errorCode switch
    {
        PackageErrorCode.E_MANIFEST_INVALID
            or PackageErrorCode.E_MIGRATION_NAME_INVALID
            or PackageErrorCode.E_MIGRATION_DUPLICATE_PREFIX => 10,
        PackageErrorCode.E_MIGRATION_MODIFIED => 11,
        PackageErrorCode.E_ALREADY_INSTALLED
            or PackageErrorCode.E_INSTALL_INTERRUPTED
            or PackageErrorCode.E_NOT_INSTALLED
            or PackageErrorCode.E_DOWNGRADE_REFUSED => 13,
        PackageErrorCode.E_TARGET_MISMATCH => 14,
        PackageErrorCode.E_MIGRATION_FAILED => 15,
        _ => 1,
    };

    // TECHNICAL.md, Exit Codes: the mapping from DEPLOY.md error codes.
    private static int ExitCodeFor(RealmErrorCode errorCode) => errorCode switch
    {
        RealmErrorCode.E_BUNDLE_INVALID => 10,
        RealmErrorCode.E_TARGET_MISMATCH
            or RealmErrorCode.E_PACKAGE_UNRESOLVED => 14,
        RealmErrorCode.E_CONFIG_KEY_MISSING
            or RealmErrorCode.E_CONFIG_FORMAT_UNEXPECTED => 3,
        RealmErrorCode.E_REALM_NOT_DEPLOYED => 13,
        RealmErrorCode.E_PACKAGE_INSTALL_FAILED
            or RealmErrorCode.E_PROVISION_FAILED
            or RealmErrorCode.E_REGISTRATION_FAILED
            or RealmErrorCode.E_CONTAINER_FAILED
            or RealmErrorCode.E_STATE_FAILED => 15,
        _ => 1,
    };
}
