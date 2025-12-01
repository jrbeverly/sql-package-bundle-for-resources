using Forge.Contracts;
using Xunit;

namespace Forge.Environments.Tests;

public class RealmPackageResolutionTests
{
    [Fact]
    public async Task FixtureBundleResolvesItsPackageFromItsOwnPackagesDirectory()
    {
        var bundle = (await RealmBundleReader.ReadAsync(RealmFixturePaths.SearingGorge, "example-app")).Value!;

        var plan = await RealmPackageResolver.ResolveAsync(RealmFixturePaths.SearingGorge, bundle, "example-app");

        Assert.Null(plan.ErrorCode);
        var package = Assert.Single(plan.Value!);
        Assert.Equal("customer-profiles", package.Manifest.Name);
        Assert.Equal("1.0.0", package.Manifest.Version);
        Assert.Equal(2, package.Migrations.Count);
    }

    [Fact]
    public async Task BothFixtureBundlesResolveTheSamePackageIndependently()
    {
        var searingGorge = (await RealmBundleReader.ReadAsync(RealmFixturePaths.SearingGorge, "example-app")).Value!;
        var blackrockDepths = (await RealmBundleReader.ReadAsync(RealmFixturePaths.BlackrockDepths, "example-app")).Value!;

        var firstPlan = await RealmPackageResolver.ResolveAsync(RealmFixturePaths.SearingGorge, searingGorge, "example-app");
        var secondPlan = await RealmPackageResolver.ResolveAsync(RealmFixturePaths.BlackrockDepths, blackrockDepths, "example-app");

        Assert.Null(firstPlan.ErrorCode);
        Assert.Null(secondPlan.ErrorCode);
        var first = Assert.Single(firstPlan.Value!);
        var second = Assert.Single(secondPlan.Value!);
        Assert.Equal("customer-profiles", first.Manifest.Name);
        Assert.Equal("customer-profiles", second.Manifest.Name);
        Assert.Equal("1.0.0", first.Manifest.Version);
        Assert.Equal("1.0.0", second.Manifest.Version);
        Assert.Equal(2, first.Migrations.Count);
        Assert.Equal(2, second.Migrations.Count);
    }

    [Fact]
    public async Task PackageMissingFromTheBundleIsUnresolved()
    {
        var bundle = (await RealmBundleReader.ReadAsync(RealmFixturePaths.SearingGorge, "example-app")).Value!;
        var bundlePath = await WriteBundleAsync(bundle with { Packages = [new RealmPackageReference("extension-framework", "2.1.0")] });

        var plan = await RealmPackageResolver.ResolveAsync(bundlePath, bundle with { Packages = [new RealmPackageReference("extension-framework", "2.1.0")] }, "example-app");

        Assert.Equal(RealmErrorCode.E_PACKAGE_UNRESOLVED, plan.ErrorCode);
        Assert.Contains("extension-framework", plan.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageVersionMismatchIsUnresolved()
    {
        var bundle = (await RealmBundleReader.ReadAsync(RealmFixturePaths.SearingGorge, "example-app")).Value!;
        var reference = new RealmPackageReference("customer-profiles", "9.9.9");

        var plan = await RealmPackageResolver.ResolveAsync(RealmFixturePaths.SearingGorge, bundle with { Packages = [reference] }, "example-app");

        Assert.Equal(RealmErrorCode.E_PACKAGE_UNRESOLVED, plan.ErrorCode);
        Assert.Contains("9.9.9", plan.Message, StringComparison.Ordinal);
        Assert.Contains("1.0.0", plan.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageWithInvalidManifestIsUnresolved()
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"forge_test_bundle_{Guid.NewGuid():N}");
        var packagePath = Path.Combine(bundlePath, "packages", "broken");
        Directory.CreateDirectory(packagePath);
        await File.WriteAllTextAsync(Path.Combine(packagePath, "manifest.yaml"), """
            apiVersion: sqlpackage.v1
            kind: Package

            metadata:
              name: broken
              version: not-a-version
              description: Broken on purpose
            """);

        var bundle = (await RealmBundleReader.ReadAsync(RealmFixturePaths.SearingGorge, "example-app")).Value!;
        var reference = new RealmPackageReference("broken", "1.0.0");
        var plan = await RealmPackageResolver.ResolveAsync(
            bundlePath, bundle with { Packages = [reference] }, "example-app");

        Assert.Equal(RealmErrorCode.E_PACKAGE_UNRESOLVED, plan.ErrorCode);
        Assert.Contains("E_MANIFEST_INVALID", plan.Message, StringComparison.Ordinal);
    }

    private static async Task<string> WriteBundleAsync(RealmBundle bundle)
    {
        var bundlePath = Path.Combine(Path.GetTempPath(), $"forge_test_bundle_{Guid.NewGuid():N}");
        Directory.CreateDirectory(bundlePath);
        var references = string.Join("\n", bundle.Packages.Select(p => $"      - name: {p.Name}\n        version: {p.Version}"));
        var manifest = $"""
            apiVersion: realm.v1
            kind: Realm

            metadata:
              name: {bundle.Name}
              displayName: {bundle.DisplayName}

            spec:
              core: {bundle.Core}
              content:
                packages:
            {references}
              realm:
                id: {bundle.RealmId}
                icon: {bundle.Icon}
                timezone: {bundle.Timezone}
                population: {bundle.Population}
            """;
        await File.WriteAllTextAsync(Path.Combine(bundlePath, "manifest.yaml"), manifest);
        return bundlePath;
    }
}
