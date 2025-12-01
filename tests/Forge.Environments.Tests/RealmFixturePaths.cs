namespace Forge.Environments.Tests;

internal static class RealmFixturePaths
{
    public static string SearingGorge => Path.Combine(ExperimentRoot, "fixtures", "realms", "searing-gorge");

    public static string BlackrockDepths => Path.Combine(ExperimentRoot, "fixtures", "realms", "blackrock-depths");

    public static string Template => Path.Combine(ExperimentRoot, "fixtures", "realms", "mangosd.conf.dist");

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
