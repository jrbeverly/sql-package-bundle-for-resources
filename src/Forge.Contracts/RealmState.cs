namespace Forge.Contracts;

// The recorded deployment state of one realm (DEPLOY.md, Deployment State).
// The database is not the source of truth for this, and this is not the
// source of truth for what is installed in the world database — the tracking
// tables are.
public sealed record RealmState(
    string Name,
    string DisplayName,
    int RealmId,
    int Port,
    string WorldDatabase,
    string CharacterDatabase,
    string Container,
    string State,
    DateTime DeployedAt);
