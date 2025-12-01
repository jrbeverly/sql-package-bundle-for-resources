using Forge.Database;
using MySqlConnector;
using Xunit;

namespace Forge.Database.Tests;

public class MigrationExecutorTests
{
    [Fact]
    public void AllowUserVariablesIsForcedOn()
    {
        var result = MigrationExecutor.WithAllowUserVariables(
            "Server=127.0.0.1;User ID=forge;Password=forge-test;Database=primary");

        Assert.True(new MySqlConnectionStringBuilder(result).AllowUserVariables);
    }

    [Fact]
    public void ExplicitFalseIsOverridden()
    {
        var result = MigrationExecutor.WithAllowUserVariables(
            "Server=127.0.0.1;User ID=forge;Password=forge-test;AllowUserVariables=False");

        Assert.True(new MySqlConnectionStringBuilder(result).AllowUserVariables);
    }

    [Fact]
    public void OtherConnectionOptionsArePreserved()
    {
        var result = MigrationExecutor.WithAllowUserVariables(
            "Server=db.example;Port=3307;User ID=forge;Password=forge-test;Database=primary");

        var builder = new MySqlConnectionStringBuilder(result);
        Assert.Equal("db.example", builder.Server);
        Assert.Equal(3307u, builder.Port);
        Assert.Equal("primary", builder.Database);
    }
}
