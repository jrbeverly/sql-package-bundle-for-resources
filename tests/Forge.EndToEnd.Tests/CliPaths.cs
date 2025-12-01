namespace Forge.EndToEnd.Tests;

internal static class CliPaths
{
    public static string ForgeBinary => Path.Combine(ExperimentRoot, "src", "Forge.Cli", "bin", "Debug", "net8.0", "forge");

    public static string ContentPackageV1 => Path.Combine(ExperimentRoot, "fixtures", "packages", "customer-profiles-v1");

    public static string ContentPackageV2 => Path.Combine(ExperimentRoot, "fixtures", "packages", "customer-profiles-v2");

    public static string RealmTemplate => Path.Combine(ExperimentRoot, "fixtures", "realms", "mangosd.conf.dist");

    public static string RealmAuthSchema => Path.Combine(ExperimentRoot, "fixtures", "auth", "realmlist.sql");

    public static string RealmSearingGorge => Path.Combine(ExperimentRoot, "fixtures", "realms", "searing-gorge");

    public static string RealmBlackrockDepths => Path.Combine(ExperimentRoot, "fixtures", "realms", "blackrock-depths");

    private static string ExperimentRoot { get; } = FindExperimentRoot();

    private static string FindExperimentRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "global.json")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("No global.json found above the test output directory; cannot locate the experiment root.");
    }
}
