using Forge.Contracts;
using Xunit;

namespace Forge.Catalog.Tests;

// Acceptance: a fresh trust store refuses the catalog; the explicit trust act
// shows the display name and the full key, and only then is the same catalog
// accepted. Verification alone never enrols.
public class TrustEnrolmentTests
{
    [Fact]
    public void AFreshTrustStoreRefusesTheCatalogAndShowsDisplayNameAndFullKey()
    {
        var keyPair = CatalogTestHelper.NewKeyPair();
        var json = CatalogTestHelper.Publish(keyPair.PrivateKeyBase64, CatalogTestHelper.Artifact("customer-profile", "1.0.0", "CREATE TABLE customer_profile (id INT);"));

        var result = CatalogVerifier.Verify(json, CatalogTestHelper.NewTrustStore());

        Assert.Equal(CatalogErrorCode.E_PUBLISHER_UNTRUSTED, result.ErrorCode);
        Assert.Null(result.Value);
        Assert.Contains(CatalogTestHelper.DisplayName, result.Message);
        Assert.Contains(keyPair.PublicKeyBase64, result.Message);
    }

    [Fact]
    public void TheExplicitTrustActShowsTheNameAndFullKeyAndAcceptsTheCatalog()
    {
        var keyPair = CatalogTestHelper.NewKeyPair();
        var json = CatalogTestHelper.Publish(keyPair.PrivateKeyBase64, CatalogTestHelper.Artifact("customer-profile", "1.0.0", "CREATE TABLE customer_profile (id INT);"));
        var store = CatalogTestHelper.NewTrustStore();
        var refused = CatalogVerifier.Verify(json, store);
        Assert.Equal(CatalogErrorCode.E_PUBLISHER_UNTRUSTED, refused.ErrorCode);

        // The explicit operator act: read the publisher block, show it, then
        // enrol. The entry carries exactly what the operator was shown.
        var publisher = CatalogReader.ReadPublisher(json);
        Assert.Null(publisher.ErrorCode);
        var entry = store.Trust(publisher.Value!);
        Assert.Equal(CatalogTestHelper.DisplayName, entry.DisplayName);
        Assert.Equal(keyPair.PublicKeyBase64, entry.Key);

        var accepted = CatalogVerifier.Verify(json, store);

        Assert.Null(accepted.ErrorCode);
        Assert.Single(accepted.Value!.Artifacts);
        Assert.Equal("customer-profile", accepted.Value.Artifacts[0].Id);
    }

    [Fact]
    public void VerificationAloneNeverEnrols()
    {
        var keyPair = CatalogTestHelper.NewKeyPair();
        var json = CatalogTestHelper.Publish(keyPair.PrivateKeyBase64, CatalogTestHelper.Artifact("customer-profile", "1.0.0", "CREATE TABLE customer_profile (id INT);"));
        var store = CatalogTestHelper.NewTrustStore();

        Assert.Equal(CatalogErrorCode.E_PUBLISHER_UNTRUSTED, CatalogVerifier.Verify(json, store).ErrorCode);
        Assert.False(store.IsTrusted(keyPair.PublicKeyBase64));
        Assert.Equal(CatalogErrorCode.E_PUBLISHER_UNTRUSTED, CatalogVerifier.Verify(json, store).ErrorCode);
    }

    [Fact]
    public void TrustPersistsAcrossStoreReloads()
    {
        var keyPair = CatalogTestHelper.NewKeyPair();
        var json = CatalogTestHelper.Publish(keyPair.PrivateKeyBase64, CatalogTestHelper.Artifact("customer-profile", "1.0.0", "CREATE TABLE customer_profile (id INT);"));
        var stateRoot = Path.Combine(Path.GetTempPath(), "forge-catalog-tests", Guid.NewGuid().ToString("N"));
        var store = TrustStore.Load(stateRoot);
        var publisher = CatalogReader.ReadPublisher(json);
        store.Trust(publisher.Value!);

        var reloaded = TrustStore.Load(stateRoot);

        Assert.True(reloaded.IsTrusted(keyPair.PublicKeyBase64));
        var accepted = CatalogVerifier.Verify(json, reloaded);
        Assert.Null(accepted.ErrorCode);
    }
}
