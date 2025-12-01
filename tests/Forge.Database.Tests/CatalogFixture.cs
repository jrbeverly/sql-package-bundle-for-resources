using Forge.Catalog;
using Forge.Contracts;
using Forge.Packaging;
using Xunit;

namespace Forge.Database.Tests;

// Builds a signed catalog and a trust store the way a publisher and an
// operator would, for tests that install through a trusted catalog.
internal static class CatalogFixture
{
    public static PublisherKeyPair NewKeyPair() => CatalogKeys.Generate();

    public static string Publish(string privateKeyBase64, params ArtifactInput[] artifacts)
    {
        var request = new PublishRequest(
            "test-publisher",
            "Test Publisher",
            privateKeyBase64,
            1,
            new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero),
            null,
            artifacts,
            []);
        var result = CatalogPublisher.Publish(request);
        Assert.Null(result.ErrorCode);
        return result.Value!;
    }

    public static ArtifactInput Artifact(string id, string version, byte[] content) =>
        new(id, version, content, CatalogPackageReader.SqlPackageMediaType);

    public static TrustStore NewStore() =>
        TrustStore.Load(Path.Combine(Path.GetTempPath(), "forge-database-tests", Guid.NewGuid().ToString("N")));

    public static TrustStore TrustedStore(string privateKeyBase64, string catalogJson)
    {
        var store = NewStore();
        var publisher = CatalogReader.ReadPublisher(catalogJson);
        Assert.Null(publisher.ErrorCode);
        store.Trust(publisher.Value!);
        return store;
    }
}
