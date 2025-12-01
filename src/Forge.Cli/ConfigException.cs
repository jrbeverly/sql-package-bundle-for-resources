namespace Forge.Cli;

// A missing or invalid config file, reported as exit 3 (TECHNICAL.md, Exit
// Codes).
internal sealed class ConfigException : Exception
{
    public ConfigException(string message)
        : base(message)
    {
    }
}
