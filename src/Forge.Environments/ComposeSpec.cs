namespace Forge.Environments;

// What a generated compose.yaml carries for one realm (DEPLOY.md,
// Containers): the operator's image, the realm's derived container name and
// allocated port, and the generated config mounted read-only. The placeholder
// image's command verifies the mounted config carries this realm's id and
// port, then stays up — a running container is proof the generated artifact
// describes this realm.
public sealed record ComposeSpec(
    string Name,
    int RealmId,
    int Port,
    string Image,
    string ConfigFile);
