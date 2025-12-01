using MySqlConnector;

namespace Forge.Database;

// Builds connection strings for tool-issued work. AllowUserVariables is
// forced on (TECHNICAL.md, Database Execution).
public static class DatabaseConnection
{
    public static string For(string host, uint port, string user, string password, string database)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = host,
            Port = port,
            UserID = user,
            Password = password,
            Database = database,
            AllowUserVariables = true,
        };
        return builder.ConnectionString;
    }
}
