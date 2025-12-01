namespace Forge.Contracts;

// A content digest of artifact bytes (SPEC.md, Artifact).
public sealed record Digest(string Algorithm, string Value);
