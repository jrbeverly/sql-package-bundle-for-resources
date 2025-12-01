using Forge.Contracts;
using Xunit;

namespace Forge.Packaging.Tests;

public class TargetMatchingTests
{
    private const string ValidManifest = """
        apiVersion: sqlpackage.v1
        kind: Package

        metadata:
          name: test-package
          version: 1.0.0
          description: A test package

        spec:
          targets:
            core: example-app
            database: primary
        """;

    [Fact]
    public async Task MismatchedCoreIsRefused()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "other-app");

        Assert.Equal(PackageErrorCode.E_TARGET_MISMATCH, result.ErrorCode);
        Assert.Contains("example-app", result.Message, StringComparison.Ordinal);
        Assert.Contains("other-app", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CoreMatchIsExact()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app-extra");

        Assert.Equal(PackageErrorCode.E_TARGET_MISMATCH, result.ErrorCode);
    }

    [Fact]
    public async Task MatchingCoreIsAccepted()
    {
        using var package = await TempPackage.CreateAsync(ValidManifest, ("001_initial.sql", "SELECT 1;"));

        var result = await PackageReader.ReadAsync(package.Path, "example-app");

        Assert.Null(result.ErrorCode);
    }
}
