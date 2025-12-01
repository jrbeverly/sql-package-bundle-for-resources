namespace Forge.Environments;

// Assigns a realm the lowest free application-server port at or above the
// configured base, where "free" means not recorded in another realm's
// deployment state (DEPLOY.md, Derived Names). A realm's own recorded port is
// reused by its deployer before allocation is consulted.
public static class RealmPortAllocator
{
    public static async Task<int> AllocateAsync(
        string stateRoot, int basePort, CancellationToken cancellationToken = default)
    {
        var taken = new HashSet<int>();
        var realmsDirectory = Path.Combine(stateRoot, "realms");
        if (Directory.Exists(realmsDirectory))
        {
            foreach (var directory in Directory.EnumerateDirectories(realmsDirectory))
            {
                var state = await RealmStateStore.TryReadAsync(stateRoot, Path.GetFileName(directory), cancellationToken);
                if (state is not null)
                {
                    taken.Add(state.Port);
                }
            }
        }

        var port = basePort;
        while (taken.Contains(port))
        {
            port++;
        }

        return port;
    }
}
