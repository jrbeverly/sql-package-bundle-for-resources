using Forge.Contracts;
using Forge.Database;

namespace Forge.Environments;

// Deploys a realm from a bundle (POC.md, claim 6, first half): validate the
// bundle, resolve its packages, generate the config, provision the world and
// character databases, install the packages into the world database with the
// layer-2 path unchanged, write the registration row, start the placeholder
// container, and record the state.
//
// Order matters for the failure contract: bundle validation, package
// resolution, and config generation happen before anything is written, so a
// refusal leaves nothing behind; from provisioning on, a failure drops the
// databases created this run and removes a deployment directory created this
// run.
public static class RealmDeployer
{
    public static async Task<RealmResult<DeployedRealm>> DeployAsync(
        string bundlePath, DeployConfig config, CancellationToken cancellationToken = default)
    {
        var bundleResult = await RealmBundleReader.ReadAsync(bundlePath, config.Core, cancellationToken);
        if (bundleResult.ErrorCode is not null)
        {
            return RealmResult<DeployedRealm>.Failure(bundleResult.ErrorCode.Value, bundleResult.Message!);
        }

        var bundle = bundleResult.Value!;

        // The full install plan is computed before anything is written; an
        // unresolvable package stops the deployment here (DEPLOY.md, Deploy).
        var planResult = await RealmPackageResolver.ResolveAsync(bundlePath, bundle, config.Core, cancellationToken);
        if (planResult.ErrorCode is not null)
        {
            return RealmResult<DeployedRealm>.Failure(planResult.ErrorCode.Value, planResult.Message!);
        }

        var worldDatabase = RealmNames.WorldDatabase(bundle.Name);
        var characterDatabase = RealmNames.CharacterDatabase(bundle.Name);
        var stateDirectory = RealmStateStore.DirectoryFor(config.StateRoot, bundle.Name);

        // A realm's recorded port is reused; otherwise the lowest free port at
        // or above the configured base is allocated (DEPLOY.md, Derived Names).
        var existing = await RealmStateStore.TryReadAsync(config.StateRoot, bundle.Name, cancellationToken);
        var port = existing?.Port ?? await RealmPortAllocator.AllocateAsync(config.StateRoot, config.BasePort, cancellationToken);

        var values = new RealmConfigValues(
            bundle.RealmId,
            port,
            ConnectionStringFor(config, config.IdentityDatabase),
            ConnectionStringFor(config, worldDatabase),
            ConnectionStringFor(config, characterDatabase));
        var configResult = await RealmConfigGenerator.GenerateAsync(config.ConfigTemplate, values, cancellationToken);
        if (configResult.ErrorCode is not null)
        {
            return RealmResult<DeployedRealm>.Failure(configResult.ErrorCode.Value, configResult.Message!);
        }

        var directoryExisted = Directory.Exists(stateDirectory);
        var connectionString = DatabaseConnection.For(config.Host, config.DatabasePort, config.User, config.Password, config.IdentityDatabase);
        var notes = new List<string>();
        try
        {
            Directory.CreateDirectory(stateDirectory);
            await WriteConfigFileAsync(stateDirectory, config.ConfigTemplate, configResult.Value!, cancellationToken);

            var worldResult = await DatabaseProvisioner.CreateDatabaseAsync(connectionString, worldDatabase, cancellationToken);
            if (worldResult.ErrorCode is not null)
            {
                return await CleanupAsync(worldResult.ErrorCode.Value, worldResult.Message!,
                    connectionString, worldDatabase, characterDatabase, stateDirectory, directoryExisted, cancellationToken);
            }

            var characterResult = await DatabaseProvisioner.CreateDatabaseAsync(connectionString, characterDatabase, cancellationToken);
            if (characterResult.ErrorCode is not null)
            {
                return await CleanupAsync(characterResult.ErrorCode.Value, characterResult.Message!,
                    connectionString, worldDatabase, characterDatabase, stateDirectory, directoryExisted, cancellationToken);
            }

            // The layer-2 install path is unchanged; an already-installed
            // package is the idempotent re-deploy case, reported as a note.
            foreach (var package in planResult.Value!)
            {
                var installed = await PackageInstaller.InstallAsync(connectionString, worldDatabase, package, Environment.UserName, cancellationToken);
                if (installed.ErrorCode is not null)
                {
                    if (installed.ErrorCode == PackageErrorCode.E_ALREADY_INSTALLED)
                    {
                        notes.Add($"package '{package.Manifest.Name}' is already installed at version {package.Manifest.Version}; nothing to do");
                        continue;
                    }

                    return await CleanupAsync(RealmErrorCode.E_PACKAGE_INSTALL_FAILED,
                        $"{installed.ErrorCode.Value}: {installed.Message}",
                        connectionString, worldDatabase, characterDatabase, stateDirectory, directoryExisted, cancellationToken);
                }
            }

            var composeResult = await ComposeWriter.WriteAsync(
                stateDirectory,
                new ComposeSpec(bundle.Name, bundle.RealmId, port, config.Image, ConfigFileNameFor(config.ConfigTemplate)),
                cancellationToken);
            if (composeResult.ErrorCode is not null)
            {
                return await CleanupAsync(composeResult.ErrorCode.Value, composeResult.Message!,
                    connectionString, worldDatabase, characterDatabase, stateDirectory, directoryExisted, cancellationToken);
            }

            var registration = new RealmRegistration(
                bundle.RealmId, bundle.DisplayName, config.AdvertisedAddress, port, bundle.Icon, bundle.Timezone, bundle.Population);
            var registered = await RealmRegistrar.RegisterAsync(connectionString, config.IdentityDatabase, registration, cancellationToken);
            if (registered.ErrorCode is not null)
            {
                return await CleanupAsync(registered.ErrorCode.Value, registered.Message!,
                    connectionString, worldDatabase, characterDatabase, stateDirectory, directoryExisted, cancellationToken);
            }

            var started = await StartContainerAsync(composeResult.Value!, RealmNames.Container(bundle.Name), cancellationToken);
            if (started.ErrorCode is not null)
            {
                return await CleanupAsync(started.ErrorCode.Value, started.Message!,
                    connectionString, worldDatabase, characterDatabase, stateDirectory, directoryExisted, cancellationToken);
            }

            var state = new RealmState(
                bundle.Name, bundle.DisplayName, bundle.RealmId, port, worldDatabase, characterDatabase,
                RealmNames.Container(bundle.Name), "running", DateTime.UtcNow);
            var stateResult = await RealmStateStore.WriteAsync(config.StateRoot, state, cancellationToken);
            if (stateResult.ErrorCode is not null)
            {
                return await CleanupAsync(stateResult.ErrorCode.Value, stateResult.Message!,
                    connectionString, worldDatabase, characterDatabase, stateDirectory, directoryExisted, cancellationToken);
            }

            return RealmResult<DeployedRealm>.Success(new DeployedRealm(state, notes));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return await CleanupAsync(RealmErrorCode.E_STATE_FAILED,
                $"deployment directory '{stateDirectory}' could not be written: {e.Message}",
                connectionString, worldDatabase, characterDatabase, stateDirectory, directoryExisted, cancellationToken);
        }
    }

    private static string ConnectionStringFor(DeployConfig config, string database) =>
        $"{config.Host};{config.DatabasePort};{config.User};{config.Password};{database}";

    // The generated config is the template's sibling name: the .dist suffix
    // marks the distributed template, and dropping it names the realm's file.
    private static string ConfigFileNameFor(string templatePath)
    {
        var fileName = Path.GetFileName(templatePath);
        return fileName.EndsWith(".dist", StringComparison.Ordinal) ? fileName[..^5] : fileName;
    }

    private static async Task WriteConfigFileAsync(
        string stateDirectory, string templatePath, string content, CancellationToken cancellationToken)
    {
        var configPath = Path.Combine(stateDirectory, ConfigFileNameFor(templatePath));
        await File.WriteAllTextAsync(configPath, content, cancellationToken);
        // Generated configs contain database credentials (DEPLOY.md,
        // Configuration): owner-only, never logged.
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(configPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static async Task<RealmResult<DeployedRealm>> CleanupAsync(
        RealmErrorCode errorCode,
        string message,
        string connectionString,
        string worldDatabase,
        string characterDatabase,
        string stateDirectory,
        bool directoryExisted,
        CancellationToken cancellationToken)
    {
        // A refused deploy over a half-applied one: the databases created this
        // run are dropped so the next deploy starts from clean state.
        _ = await DatabaseProvisioner.DropDatabaseAsync(connectionString, worldDatabase, cancellationToken);
        _ = await DatabaseProvisioner.DropDatabaseAsync(connectionString, characterDatabase, cancellationToken);
        if (!directoryExisted && Directory.Exists(stateDirectory))
        {
            Directory.Delete(stateDirectory, recursive: true);
        }

        return RealmResult<DeployedRealm>.Failure(errorCode, message);
    }

    private static async Task<RealmResult<bool>> StartContainerAsync(
        string composePath, string containerName, CancellationToken cancellationToken)
    {
        var up = await ProcessRunner.RunAsync("docker", ["compose", "-f", composePath, "up", "-d"], cancellationToken);
        if (up.ExitCode != 0)
        {
            return RealmResult<bool>.Failure(
                RealmErrorCode.E_CONTAINER_FAILED, $"docker compose up failed: {up.Output.Trim()}");
        }

        // The placeholder container's command verifies the mounted config
        // carries this realm's values and then stays up; running is the proof.
        var inspect = await ProcessRunner.RunAsync("docker", ["inspect", "-f", "{{.State.Running}}", containerName], cancellationToken);
        if (inspect.ExitCode != 0 || inspect.Output.Trim() != "true")
        {
            return RealmResult<bool>.Failure(
                RealmErrorCode.E_CONTAINER_FAILED,
                $"container '{containerName}' is not running after start: {inspect.Output.Trim()}");
        }

        return RealmResult<bool>.Success(true);
    }
}
