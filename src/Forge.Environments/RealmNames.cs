namespace Forge.Environments;

// Every name a realm deployment creates, derived from the bundle's
// metadata.name so a realm can be identified and cleaned up without a lookup
// table (DEPLOY.md, Derived Names). Underscores in database names and hyphens
// in the container name because MySQL identifiers and container names have
// different conventions.
public static class RealmNames
{
    public static string WorldDatabase(string name) => $"world_{name}";

    public static string CharacterDatabase(string name) => $"character_{name}";

    public static string Container(string name) => $"realm-{name}";
}
