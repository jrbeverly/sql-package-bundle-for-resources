namespace Forge.Environments;

// The operator-supplied inputs a realm deployment needs (TECHNICAL.md,
// Configuration, realm-named). The advertised address comes from
// configuration, never from a local interface: getting it wrong produces a
// realm that appears in the list and cannot be joined.
public sealed record DeployConfig(
    string Core,
    string Host,
    uint DatabasePort,
    string User,
    string Password,
    string IdentityDatabase,
    string StateRoot,
    string ConfigTemplate,
    string Image,
    int BasePort,
    string AdvertisedAddress);
