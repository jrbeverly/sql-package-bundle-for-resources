namespace Forge.Contracts;

// An artifact entry in a catalog: identity, version, and the digest and byte
// length of the artifact's bytes (SPEC.md, Artifact). The catalog layer holds
// no other notion of what those bytes are; MediaType is the label the layer
// above interprets.
public sealed record Artifact(string Id, string Version, Digest Digest, long Size, string? MediaType);
