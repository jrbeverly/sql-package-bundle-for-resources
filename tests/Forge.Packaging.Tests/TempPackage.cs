namespace Forge.Packaging.Tests;

// A package directory under the system temp path with the given manifest and
// migration files, deleted on dispose. A null manifest omits manifest.yaml.
internal sealed class TempPackage : IDisposable
{
    private TempPackage(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static async Task<TempPackage> CreateAsync(string? manifest, params (string Name, string Content)[] migrations)
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"forge_test_pkg_{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(path, "migrations"));
        if (manifest is not null)
        {
            await File.WriteAllTextAsync(System.IO.Path.Combine(path, "manifest.yaml"), manifest);
        }

        foreach (var (name, content) in migrations)
        {
            await File.WriteAllTextAsync(System.IO.Path.Combine(path, "migrations", name), content);
        }

        return new TempPackage(path);
    }

    public void Dispose()
    {
        System.IO.Directory.Delete(Path, recursive: true);
    }
}
