namespace Forge.Contracts;

// A validated realm bundle (DEPLOY.md, Environment Bundle, realm-named).
// spec.realm carries the realm id and the pass-through fields the tool writes
// into the registration row without interpreting them.
public sealed record RealmBundle(
    string Name,
    string DisplayName,
    string Core,
    IReadOnlyList<RealmPackageReference> Packages,
    int RealmId,
    int Icon,
    int Timezone,
    double Population);
