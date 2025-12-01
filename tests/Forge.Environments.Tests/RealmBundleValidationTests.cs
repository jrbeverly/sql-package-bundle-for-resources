using Forge.Contracts;
using Xunit;

namespace Forge.Environments.Tests;

public class RealmBundleValidationTests
{
    [Fact]
    public async Task ValidRealmBundleReads()
    {
        var bundle = await RealmBundleReader.ReadAsync(RealmFixturePaths.SearingGorge, "example-app");

        Assert.Null(bundle.ErrorCode);
        Assert.Equal("searing-gorge", bundle.Value!.Name);
        Assert.Equal("Searing Gorge", bundle.Value.DisplayName);
        Assert.Equal("example-app", bundle.Value.Core);
        Assert.Equal(new RealmPackageReference("customer-profiles", "1.0.0"), Assert.Single(bundle.Value.Packages));
        Assert.Equal(1, bundle.Value.RealmId);
        Assert.Equal(1, bundle.Value.Icon);
        Assert.Equal(0, bundle.Value.Timezone);
        Assert.Equal(0, bundle.Value.Population);
    }

    [Fact]
    public async Task SecondRealmBundleReadsWithItsOwnIdentityAndTheSamePackageReference()
    {
        var bundle = await RealmBundleReader.ReadAsync(RealmFixturePaths.BlackrockDepths, "example-app");

        Assert.Null(bundle.ErrorCode);
        Assert.Equal("blackrock-depths", bundle.Value!.Name);
        Assert.Equal("Blackrock Depths", bundle.Value.DisplayName);
        Assert.Equal("example-app", bundle.Value.Core);
        Assert.Equal(new RealmPackageReference("customer-profiles", "1.0.0"), Assert.Single(bundle.Value.Packages));
        Assert.Equal(2, bundle.Value.RealmId);
        Assert.Equal(1, bundle.Value.Icon);
        Assert.Equal(0, bundle.Value.Timezone);
        Assert.Equal(0, bundle.Value.Population);
    }

    [Fact]
    public async Task BundleWithWrongApiVersionIsRejected()
    {
        var bundle = await WriteBundleAsync("""
            apiVersion: environment.v1
            kind: Realm

            metadata:
              name: searing-gorge
              displayName: Searing Gorge

            spec:
              core: example-app
              content:
                packages:
                  - name: customer-profiles
                    version: 1.0.0
              realm:
                id: 1
                icon: 0
                timezone: 0
                population: 0
            """);

        var result = await RealmBundleReader.ReadAsync(bundle, "example-app");

        Assert.Equal(RealmErrorCode.E_BUNDLE_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task BundleWithUnknownFieldIsRejected()
    {
        var bundle = await WriteBundleAsync("""
            apiVersion: realm.v1
            kind: Realm

            metadata:
              name: searing-gorge
              displayName: Searing Gorge

            spec:
              core: example-app
              content:
                packages:
                  - name: customer-profiles
                    version: 1.0.0
              realm:
                id: 1
                icon: 0
                timezone: 0
                population: 0
                realmflags: 2
            """);

        var result = await RealmBundleReader.ReadAsync(bundle, "example-app");

        Assert.Equal(RealmErrorCode.E_BUNDLE_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task BundleWithInvalidNameIsRejected()
    {
        var bundle = await WriteBundleAsync("""
            apiVersion: realm.v1
            kind: Realm

            metadata:
              name: Searing_Gorge
              displayName: Searing Gorge

            spec:
              core: example-app
              content:
                packages:
                  - name: customer-profiles
                    version: 1.0.0
              realm:
                id: 1
                icon: 0
                timezone: 0
                population: 0
            """);

        var result = await RealmBundleReader.ReadAsync(bundle, "example-app");

        Assert.Equal(RealmErrorCode.E_BUNDLE_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task BundleWithWrongCoreIsRefused()
    {
        var bundle = await WriteBundleAsync("""
            apiVersion: realm.v1
            kind: Realm

            metadata:
              name: searing-gorge
              displayName: Searing Gorge

            spec:
              core: other-app
              content:
                packages:
                  - name: customer-profiles
                    version: 1.0.0
              realm:
                id: 1
                icon: 0
                timezone: 0
                population: 0
            """);

        var result = await RealmBundleReader.ReadAsync(bundle, "example-app");

        Assert.Equal(RealmErrorCode.E_TARGET_MISMATCH, result.ErrorCode);
    }

    [Fact]
    public async Task BundleWithoutPackagesIsRejected()
    {
        var bundle = await WriteBundleAsync("""
            apiVersion: realm.v1
            kind: Realm

            metadata:
              name: searing-gorge
              displayName: Searing Gorge

            spec:
              core: example-app
              realm:
                id: 1
                icon: 0
                timezone: 0
                population: 0
            """);

        var result = await RealmBundleReader.ReadAsync(bundle, "example-app");

        Assert.Equal(RealmErrorCode.E_BUNDLE_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task BundleWithMissingRealmIdIsRejected()
    {
        var bundle = await WriteBundleAsync("""
            apiVersion: realm.v1
            kind: Realm

            metadata:
              name: searing-gorge
              displayName: Searing Gorge

            spec:
              core: example-app
              content:
                packages:
                  - name: customer-profiles
                    version: 1.0.0
              realm:
                icon: 0
                timezone: 0
                population: 0
            """);

        var result = await RealmBundleReader.ReadAsync(bundle, "example-app");

        Assert.Equal(RealmErrorCode.E_BUNDLE_INVALID, result.ErrorCode);
    }

    [Fact]
    public async Task BundleWithOutOfRangeRealmIdIsRejected()
    {
        var bundle = await WriteBundleAsync("""
            apiVersion: realm.v1
            kind: Realm

            metadata:
              name: searing-gorge
              displayName: Searing Gorge

            spec:
              core: example-app
              content:
                packages:
                  - name: customer-profiles
                    version: 1.0.0
              realm:
                id: 300
                icon: 0
                timezone: 0
                population: 0
            """);

        var result = await RealmBundleReader.ReadAsync(bundle, "example-app");

        Assert.Equal(RealmErrorCode.E_BUNDLE_INVALID, result.ErrorCode);
    }

    private static async Task<string> WriteBundleAsync(string manifest)
    {
        var bundle = Path.Combine(Path.GetTempPath(), $"forge_test_bundle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundle);
        await File.WriteAllTextAsync(Path.Combine(bundle, "manifest.yaml"), manifest);
        return bundle;
    }
}
