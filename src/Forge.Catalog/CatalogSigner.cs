using Forge.Contracts;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Forge.Catalog;

// Ed25519 (RFC 8032) signing of canonical catalog bytes. Keys and signatures
// use the base64 wire form SPEC.md specifies (SPEC.md, Signing).
public static class CatalogSigner
{
    public static CatalogSignature Sign(byte[] signingInput, string privateKeyBase64)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, new Ed25519PrivateKeyParameters(Convert.FromBase64String(privateKeyBase64), 0));
        signer.BlockUpdate(signingInput, 0, signingInput.Length);
        return new CatalogSignature("ed25519", Convert.ToBase64String(signer.GenerateSignature()));
    }

    public static bool Verify(byte[] signingInput, CatalogSignature signature, string publicKeyBase64)
    {
        var verifier = new Ed25519Signer();
        verifier.Init(false, new Ed25519PublicKeyParameters(Convert.FromBase64String(publicKeyBase64), 0));
        verifier.BlockUpdate(signingInput, 0, signingInput.Length);
        return verifier.VerifySignature(Convert.FromBase64String(signature.Value));
    }
}
