namespace Forge.Database.Tests;

internal static class FixturePaths
{
    public static string ContentPackageV1 => Path.Combine(ExperimentRoot, "fixtures", "packages", "customer-profiles-v1");

    public static string ContentPackageV2 => Path.Combine(ExperimentRoot, "fixtures", "packages", "customer-profiles-v2");

    public static string IndependentPackage => Path.Combine(ExperimentRoot, "fixtures", "packages", "audit-schema-v1");

    public static string ReportingPackage => Path.Combine(ExperimentRoot, "fixtures", "packages", "reporting-schema-v1");

    public static string ReportingPackageFixed => Path.Combine(ExperimentRoot, "fixtures", "packages", "reporting-schema-fixed");

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
