using System.Text.Json;
using Forge.Contracts;

namespace Forge.Environments;

// Reads and writes a realm's recorded deployment state (DEPLOY.md,
// Deployment State). The database is not the source of truth for this, and
// state.json is not the source of truth for what is installed in the world
// database — the tracking tables are.
public static class RealmStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public static string DirectoryFor(string stateRoot, string name) => Path.Combine(stateRoot, "realms", name);

    public static async Task<RealmState?> TryReadAsync(
        string stateRoot, string name, CancellationToken cancellationToken = default)
    {
        var statePath = Path.Combine(DirectoryFor(stateRoot, name), "state.json");
        if (!File.Exists(statePath))
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(statePath, cancellationToken);
            return JsonSerializer.Deserialize<RealmState>(json, SerializerOptions);
        }
        catch (Exception)
        {
            // A missing or unreadable state file means this realm has no
            // recorded deployment to reuse, not that the deploy must stop.
            return null;
        }
    }

    public static async Task<RealmResult<RealmState>> WriteAsync(
        string stateRoot, RealmState state, CancellationToken cancellationToken = default)
    {
        var directory = DirectoryFor(stateRoot, state.Name);
        var statePath = Path.Combine(directory, "state.json");
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(statePath, JsonSerializer.Serialize(state, SerializerOptions), cancellationToken);
            return RealmResult<RealmState>.Success(state);
        }
        catch (Exception e)
        {
            return RealmResult<RealmState>.Failure(
                RealmErrorCode.E_STATE_FAILED, $"could not record deployment state at '{statePath}': {e.Message}");
        }
    }
}
