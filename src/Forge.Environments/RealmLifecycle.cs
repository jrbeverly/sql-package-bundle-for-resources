using Forge.Contracts;

namespace Forge.Environments;

// Starts and stops one realm's container without touching anything else
// (DEPLOY.md, Start And Stop; POC.md, claim 6, second half). Both operations
// drive the realm's own compose.yaml, are idempotent, and refuse a realm that
// has no recorded deployment. Stopping one realm leaves the others running
// because no state outside its own deployment directory is read or written.
public static class RealmLifecycle
{
    public static async Task<RealmResult<RealmState>> StopAsync(
        string stateRoot, string name, CancellationToken cancellationToken = default)
    {
        var state = await RealmStateStore.TryReadAsync(stateRoot, name, cancellationToken);
        if (state is null)
        {
            return RealmResult<RealmState>.Failure(
                RealmErrorCode.E_REALM_NOT_DEPLOYED,
                $"realm '{name}' is not deployed: no state recorded under '{RealmStateStore.DirectoryFor(stateRoot, name)}'");
        }

        var composePath = Path.Combine(RealmStateStore.DirectoryFor(stateRoot, name), "compose.yaml");
        var stop = await ProcessRunner.RunAsync("docker", ["compose", "-f", composePath, "stop"], cancellationToken);
        if (stop.ExitCode != 0)
        {
            return RealmResult<RealmState>.Failure(
                RealmErrorCode.E_CONTAINER_FAILED, $"docker compose stop failed: {stop.Output.Trim()}");
        }

        var stopped = state with { State = "stopped" };
        return await RealmStateStore.WriteAsync(stateRoot, stopped, cancellationToken);
    }

    public static async Task<RealmResult<RealmState>> StartAsync(
        string stateRoot, string name, CancellationToken cancellationToken = default)
    {
        var state = await RealmStateStore.TryReadAsync(stateRoot, name, cancellationToken);
        if (state is null)
        {
            return RealmResult<RealmState>.Failure(
                RealmErrorCode.E_REALM_NOT_DEPLOYED,
                $"realm '{name}' is not deployed: no state recorded under '{RealmStateStore.DirectoryFor(stateRoot, name)}'");
        }

        var composePath = Path.Combine(RealmStateStore.DirectoryFor(stateRoot, name), "compose.yaml");
        var up = await ProcessRunner.RunAsync("docker", ["compose", "-f", composePath, "up", "-d"], cancellationToken);
        if (up.ExitCode != 0)
        {
            return RealmResult<RealmState>.Failure(
                RealmErrorCode.E_CONTAINER_FAILED, $"docker compose up failed: {up.Output.Trim()}");
        }

        var inspect = await ProcessRunner.RunAsync("docker", ["inspect", "-f", "{{.State.Running}}", state.Container], cancellationToken);
        if (inspect.ExitCode != 0 || inspect.Output.Trim() != "true")
        {
            return RealmResult<RealmState>.Failure(
                RealmErrorCode.E_CONTAINER_FAILED,
                $"container '{state.Container}' is not running after start: {inspect.Output.Trim()}");
        }

        var started = state with { State = "running" };
        return await RealmStateStore.WriteAsync(stateRoot, started, cancellationToken);
    }
}
