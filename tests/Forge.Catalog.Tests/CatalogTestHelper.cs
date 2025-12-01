using Xunit;

namespace Forge.Catalog.Tests;

// Builds a signed catalog the way a publisher does, for tests that need one.
internal static class CatalogTestHelper
{
    public const string DisplayName = "Test Publisher";

    public static PublisherKeyPair NewKeyPair() => CatalogKeys.Generate();

    public static string Publish(string privateKeyBase64, params ArtifactInput[] artifacts) =>
        Publish(privateKeyBase64, artifacts, version: 1);

    public static string Publish(string privateKeyBase64, ArtifactInput[] artifacts, long version)
    {
        var request = new PublishRequest(
            "test-publisher",
            DisplayName,
            privateKeyBase64,
            version,
            new DateTimeOffset(2026, 7, 18, 0, 0, 0, TimeSpan.Zero),
            null,
            artifacts,
            []);
        var result = CatalogPublisher.Publish(request);
        Assert.Null(result.ErrorCode);
        return result.Value!;
    }

    public static TrustStore NewTrustStore() =>
        TrustStore.Load(Path.Combine(Path.GetTempPath(), "forge-catalog-tests", Guid.NewGuid().ToString("N")));

    public static TrustStore TrustedStore(string privateKeyBase64, string catalogJson)
    {
        var store = NewTrustStore();
        var publisher = CatalogReader.ReadPublisher(catalogJson);
        Assert.Null(publisher.ErrorCode);
        store.Trust(publisher.Value!);
        return store;
    }

    public static ArtifactInput Artifact(string id, string version, string content, string? mediaType = null) =>
        new(id, version, System.Text.Encoding.UTF8.GetBytes(content), mediaType);
}
