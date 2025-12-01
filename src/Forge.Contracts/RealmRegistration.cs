namespace Forge.Contracts;

// The values written to one realmlist row in the shared authentication
// database (DEPLOY.md, Registration). Id, name, address, and port identify
// the realm for clients; icon, timezone, and population are the bundle's
// pass-through fields.
public sealed record RealmRegistration(
    int RealmId,
    string Name,
    string Address,
    int Port,
    int Icon,
    int Timezone,
    double Population);
