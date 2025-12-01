using Xunit;

namespace Forge.Packaging.Tests;

public class ContentPackageFixtureTests
{
    [Fact]
    public void VersionTwoSharesEarlierMigrationsByteForByte()
    {
        foreach (var v1File in Directory.GetFiles(MigrationsDir(FixturePaths.ContentPackageV1)))
        {
            var name = Path.GetFileName(v1File);
            var v2File = Path.Combine(MigrationsDir(FixturePaths.ContentPackageV2), name);
            Assert.True(File.Exists(v2File), $"v2 is missing migration {name}");
            Assert.Equal(File.ReadAllBytes(v1File), File.ReadAllBytes(v2File));
        }
    }

    [Fact]
    public void VersionTwoAddsExactlyOneMigration()
    {
        var v1 = MigrationNames(FixturePaths.ContentPackageV1);
        var v2 = MigrationNames(FixturePaths.ContentPackageV2);

        Assert.Single(v2.Except(v1));
        Assert.Empty(v1.Except(v2));
    }

    [Fact]
    public void VersionTwoChangesOnlyTheVersionField()
    {
        foreach (var v1File in Directory.GetFiles(FixturePaths.ContentPackageV1, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(FixturePaths.ContentPackageV1, v1File);
            if (relative == "manifest.yaml")
            {
                continue;
            }

            var v2File = Path.Combine(FixturePaths.ContentPackageV2, relative);
            Assert.True(File.Exists(v2File), $"v2 is missing {relative}");
            Assert.Equal(File.ReadAllBytes(v1File), File.ReadAllBytes(v2File));
        }

        var v1Manifest = File.ReadAllText(Path.Combine(FixturePaths.ContentPackageV1, "manifest.yaml"));
        var v2Manifest = File.ReadAllText(Path.Combine(FixturePaths.ContentPackageV2, "manifest.yaml"));
        Assert.Equal(v1Manifest.Replace("version: 1.0.0", "version: 1.1.0", StringComparison.Ordinal), v2Manifest);
    }

    [Fact]
    public void EveryFixturePackageHasAManifestAndMigrations()
    {
        var packages = Directory.GetDirectories(Path.Combine(FixturePaths.ExperimentRoot, "fixtures", "packages"));
        Assert.NotEmpty(packages);
        foreach (var package in packages)
        {
            Assert.True(File.Exists(Path.Combine(package, "manifest.yaml")), $"{Path.GetFileName(package)} has no manifest.yaml");
            Assert.NotEmpty(Directory.GetFiles(Path.Combine(package, "migrations")));
        }
    }

    private static string MigrationsDir(string package) => Path.Combine(package, "migrations");

    private static string[] MigrationNames(string package) =>
        Directory.GetFiles(MigrationsDir(package)).Select(f => Path.GetFileName(f)!).ToArray();
}
