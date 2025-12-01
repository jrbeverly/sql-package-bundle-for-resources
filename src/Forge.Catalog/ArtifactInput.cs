namespace Forge.Catalog;

// One artifact's bytes with its naming. Id and version are supplied by the
// publisher alongside the bytes, never parsed out of a filename
// (SPEC.md, Publishing). The bytes are opaque: SQL or not is a question the
// catalog layer does not ask, and MediaType is a label it does not interpret.
public sealed record ArtifactInput(string Id, string Version, byte[] Content, string? MediaType);
