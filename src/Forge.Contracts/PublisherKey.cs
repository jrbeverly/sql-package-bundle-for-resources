namespace Forge.Contracts;

// A publisher's public key. `Key` is the publisher's identity for every
// comparison, cache key, and trust decision; `Type` names the algorithm
// (SPEC.md, Publisher).
public sealed record PublisherKey(string Type, string Key);
