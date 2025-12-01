namespace Forge.Environments;

// The values the realm layer substitutes into a config template's realm-
// specific keys (DEPLOY.md, Configuration). The connection strings use the
// mangosd host;port;user;password;database form, which is the template
// format this layer reads and writes.
public sealed record RealmConfigValues(
    int RealmId,
    int Port,
    string IdentityConnectionString,
    string WorldConnectionString,
    string CharacterConnectionString);
