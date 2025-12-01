using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace Forge.Catalog;

// Ed25519 keypair generation (HLD.md: Ed25519 via BouncyCastle, since .NET 8
// has no Ed25519).
public static class CatalogKeys
{
    public static PublisherKeyPair Generate()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var keyPair = generator.GenerateKeyPair();
        var publicKey = (Ed25519PublicKeyParameters)keyPair.Public;
        var privateKey = (Ed25519PrivateKeyParameters)keyPair.Private;
        return new PublisherKeyPair(
            Convert.ToBase64String(publicKey.GetEncoded()),
            Convert.ToBase64String(privateKey.GetEncoded()));
    }

    // Derives the public half, so publishing only needs the private key.
    public static string PublicKeyOf(string privateKeyBase64)
    {
        var privateKey = new Ed25519PrivateKeyParameters(Convert.FromBase64String(privateKeyBase64), 0);
        return Convert.ToBase64String(privateKey.GeneratePublicKey().GetEncoded());
    }
}
