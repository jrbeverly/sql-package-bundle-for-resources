using System.Diagnostics;

namespace Forge.Environments;

// Runs an external process with both streams captured, shared by the realm
// deploy and lifecycle paths (DEPLOY.md, Containers: the tool drives docker
// compose and nothing else). A process that outlives its deadline is killed
// rather than left running behind a failed operation.
internal static class ProcessRunner
{
    public static async Task<(int ExitCode, string Output)> RunAsync(
        string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"could not start {fileName}");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromMinutes(10), cancellationToken);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            return (1, $"{fileName} did not finish within 10 minutes");
        }

        return (process.ExitCode, $"{await stdout}\n{await stderr}");
    }
}
