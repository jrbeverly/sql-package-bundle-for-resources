namespace Forge.Cli;

// The slice of the config format (TECHNICAL.md, Configuration) that the CLI
// commands read: the database block for package commands, plus the realm
// block for realm deployment.
internal sealed record ForgeConfig(
    string Core,
    string Host,
    uint Port,
    string User,
    string Password,
    string Primary,
    string? Analytics,
    string? Identity,
    string? RealmTemplate,
    string? RealmImage,
    int? RealmBasePort,
    string? RealmAdvertisedAddress);
