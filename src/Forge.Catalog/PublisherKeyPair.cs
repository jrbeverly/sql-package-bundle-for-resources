namespace Forge.Catalog;

// An Ed25519 keypair in the base64 wire form SPEC.md uses (RFC 4648 section 4,
// with padding). The public key is the publisher's identity; the private key
// must never appear in a catalog, in output, or in an error message
// (SPEC.md, Publishing).
public sealed record PublisherKeyPair(string PublicKeyBase64, string PrivateKeyBase64);
