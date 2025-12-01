namespace Forge.Contracts;

// A catalog publisher. `Id` and `DisplayName` are labels for humans and are
// never used as identity; `PublicKey` is (SPEC.md, Publisher).
public sealed record Publisher(string Id, string DisplayName, PublisherKey PublicKey);
