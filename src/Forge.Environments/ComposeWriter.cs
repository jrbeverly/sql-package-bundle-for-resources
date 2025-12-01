using Forge.Contracts;

namespace Forge.Environments;

// Generates the compose.yaml a realm is run with (DEPLOY.md, Containers).
// The tool drives docker compose and never the Docker API, so a deployment
// stays inspectable and reproducible by hand. The generated config is mounted
// read-only: it is the realm's own artifact and the container must not write
// to it.
public static class ComposeWriter
{
    public static async Task<RealmResult<string>> WriteAsync(
        string directory, ComposeSpec spec, CancellationToken cancellationToken = default)
    {
        var compose = $"""
            services:
              realm:
                image: "{spec.Image}"
                container_name: {RealmNames.Container(spec.Name)}
                ports:
                  - "{spec.Port}:{spec.Port}"
                volumes:
                  - ./{spec.ConfigFile}:/etc/mangos/mangosd.conf:ro
                command: ["sh", "-c", "grep -q '^RealmID = {spec.RealmId}$' /etc/mangos/mangosd.conf && grep -q '^WorldServerPort = {spec.Port}$' /etc/mangos/mangosd.conf && echo 'realm {spec.Name} ready' && sleep infinity"]
            """;
        var composePath = Path.Combine(directory, "compose.yaml");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(composePath, compose, cancellationToken);
            return RealmResult<string>.Success(composePath);
        }
        catch (Exception e)
        {
            return RealmResult<string>.Failure(
                RealmErrorCode.E_CONTAINER_FAILED, $"could not write '{composePath}': {e.Message}");
        }
    }
}
