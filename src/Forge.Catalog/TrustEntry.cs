namespace Forge.Catalog;

// One trusted publisher in the trust store. `Key` is the full base64 public
// key — the map key of the store, never the publisher id, which is not unique
// (SPEC.md, Trust Model; TECHNICAL.md, State On Disk).
public sealed record TrustEntry(string Key, string Algorithm, string DisplayName, DateTimeOffset GrantedAt);
