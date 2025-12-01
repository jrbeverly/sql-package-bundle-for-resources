using System.Reflection;
using Xunit;

namespace Forge.Architecture.Tests;

public class ProjectDependencyTests
{
    private static readonly string[] SourceProjects = ["Forge.Contracts", "Forge.Packaging", "Forge.Catalog", "Forge.Database", "Forge.Environments"];

    [Fact]
    public void ContractsDependsOnNothingButTheBcl()
    {
        // HLD.md, Project Dependency Rules: the shared vocabulary must not
        // reach anywhere, or a dependency here reaches everywhere.
        Assert.Empty(ForgeReferencesOf("Forge.Contracts"));
    }

    [Fact]
    public void CatalogDoesNotReferencePackagingOrDatabase()
    {
        var references = ForgeReferencesOf("Forge.Catalog");
        Assert.DoesNotContain("Forge.Packaging", references);
        Assert.DoesNotContain("Forge.Database", references);
    }

    [Fact]
    public void PackagingDoesNotReferenceDatabase()
    {
        var references = ForgeReferencesOf("Forge.Packaging");
        Assert.DoesNotContain("Forge.Database", references);
    }

    [Fact]
    public void NothingReferencesCli()
    {
        // HLD.md, Project Dependency Rules: Forge.Cli is the entry point. Its
        // assembly is named `forge` (TECHNICAL.md, one CLI executable).
        foreach (var project in SourceProjects)
        {
            Assert.DoesNotContain("forge", ForgeReferencesOf(project));
        }
    }

    [Fact]
    public void NoSourceProjectReferencesATestProject()
    {
        foreach (var project in SourceProjects)
        {
            var references = ForgeReferencesOf(project);
            Assert.DoesNotContain(references, r => r.EndsWith(".Tests", StringComparison.Ordinal));
        }
    }

    private static string[] ForgeReferencesOf(string assemblyName)
    {
        var path = Path.Combine(ExperimentRoot, "src", assemblyName, "bin", "Debug", "net8.0", $"{assemblyName}.dll");
        Assert.True(File.Exists(path), $"Expected {assemblyName}.dll at {path}; run the build before the tests (make validate).");
        return Assembly.LoadFrom(path).GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(n => n.StartsWith("Forge.", StringComparison.Ordinal))
            .ToArray();
    }

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
