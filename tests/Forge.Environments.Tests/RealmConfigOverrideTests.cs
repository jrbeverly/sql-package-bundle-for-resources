using Forge.Contracts;
using Xunit;

namespace Forge.Environments.Tests;

public class RealmConfigOverrideTests
{
    [Fact]
    public async Task GeneratedConfigDiffersFromTemplateOnlyInTheRealmSpecificKeys()
    {
        var values = new RealmConfigValues(
            RealmId: 7,
            Port: 9400,
            IdentityConnectionString: "db.example;3306;forge;secret;realmd",
            WorldConnectionString: "db.example;3306;forge;secret;world_searing-gorge",
            CharacterConnectionString: "db.example;3306;forge;secret;character_searing-gorge");
        var generated = await RealmConfigGenerator.GenerateAsync(RealmFixturePaths.Template, values);

        Assert.Null(generated.ErrorCode);
        var templateLines = SplitLines(await File.ReadAllTextAsync(RealmFixturePaths.Template));
        var generatedLines = SplitLines(generated.Value!);

        // Every line that is not one of the five overridden keys must be
        // byte-for-byte identical, comments included; the key lines keep the
        // template's own prefix and only change their value.
        Assert.Equal(templateLines.Length, generatedLines.Length);
        for (var i = 0; i < templateLines.Length; i++)
        {
            var key = OverriddenKeyOf(templateLines[i]);
            if (key is null)
            {
                Assert.Equal(templateLines[i], generatedLines[i]);
            }
            else
            {
                var valueStart = templateLines[i].IndexOf("= ", StringComparison.Ordinal) + 2;
                Assert.StartsWith(templateLines[i][..valueStart], generatedLines[i], StringComparison.Ordinal);
                Assert.Equal(ExpectedValueFor(key, values), generatedLines[i][valueStart..]);
            }
        }
    }

    [Fact]
    public async Task GeneratedConfigCarriesTheRealmValues()
    {
        var generated = await GenerateFromFixtureTemplateAsync(new RealmConfigValues(
            RealmId: 7,
            Port: 9400,
            IdentityConnectionString: "db.example;3306;forge;secret;realmd",
            WorldConnectionString: "db.example;3306;forge;secret;world_searing-gorge",
            CharacterConnectionString: "db.example;3306;forge;secret;character_searing-gorge"));

        Assert.Null(generated.ErrorCode);
        Assert.Contains("RealmID = 7\n", generated.Value!, StringComparison.Ordinal);
        Assert.Contains("WorldServerPort = 9400\n", generated.Value!, StringComparison.Ordinal);
        Assert.Contains(
            "LoginDatabaseInfo     = \"db.example;3306;forge;secret;realmd\"",
            generated.Value!, StringComparison.Ordinal);
        Assert.Contains(
            "WorldDatabaseInfo     = \"db.example;3306;forge;secret;world_searing-gorge\"",
            generated.Value!, StringComparison.Ordinal);
        Assert.Contains(
            "CharacterDatabaseInfo = \"db.example;3306;forge;secret;character_searing-gorge\"",
            generated.Value!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemplateMissingAKeyIsRefused()
    {
        var template = Path.Combine(Path.GetTempPath(), $"forge_test_template_{Guid.NewGuid():N}.conf");
        await File.WriteAllTextAsync(template, """
            RealmID = 1
            LoginDatabaseInfo = "127.0.0.1;3306;mangos;mangos;realmd"
            WorldDatabaseInfo = "127.0.0.1;3306;mangos;mangos;mangos"
            CharacterDatabaseInfo = "127.0.0.1;3306;mangos;mangos;characters"
            """);

        var result = await RealmConfigGenerator.GenerateAsync(template, SampleValues());

        Assert.Equal(RealmErrorCode.E_CONFIG_KEY_MISSING, result.ErrorCode);
        Assert.Contains("WorldServerPort", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TemplateWithNonIntegerPortIsRefused()
    {
        var template = await WriteTemplateAsync("WorldServerPort", "WorldServerPort = eighty-eighty-five");

        var result = await RealmConfigGenerator.GenerateAsync(template, SampleValues());

        Assert.Equal(RealmErrorCode.E_CONFIG_FORMAT_UNEXPECTED, result.ErrorCode);
    }

    [Fact]
    public async Task TemplateWithMalformedConnectionStringIsRefused()
    {
        var template = await WriteTemplateAsync("LoginDatabaseInfo", "LoginDatabaseInfo = \"127.0.0.1:3306\"");

        var result = await RealmConfigGenerator.GenerateAsync(template, SampleValues());

        Assert.Equal(RealmErrorCode.E_CONFIG_FORMAT_UNEXPECTED, result.ErrorCode);
    }

    [Fact]
    public async Task TemplateWithCrlfLineEndingsIsPreserved()
    {
        var template = Path.Combine(Path.GetTempPath(), $"forge_test_template_{Guid.NewGuid():N}.conf");
        await File.WriteAllTextAsync(template,
            "RealmID = 1\r\n" +
            "LoginDatabaseInfo = \"127.0.0.1;3306;mangos;mangos;realmd\"\r\n" +
            "WorldDatabaseInfo = \"127.0.0.1;3306;mangos;mangos;mangos\"\r\n" +
            "CharacterDatabaseInfo = \"127.0.0.1;3306;mangos;mangos;characters\"\r\n" +
            "WorldServerPort = 8085\r\n" +
            "# a comment\r\n");

        var result = await RealmConfigGenerator.GenerateAsync(template, SampleValues());

        Assert.Null(result.ErrorCode);
        Assert.Equal(
            "RealmID = 7\r\n" +
            "LoginDatabaseInfo = \"db;3306;forge;secret;realmd\"\r\n" +
            "WorldDatabaseInfo = \"db;3306;forge;secret;world\"\r\n" +
            "CharacterDatabaseInfo = \"db;3306;forge;secret;character\"\r\n" +
            "WorldServerPort = 9400\r\n" +
            "# a comment\r\n",
            result.Value);
    }

    private static async Task<RealmResult<string>> GenerateFromFixtureTemplateAsync(RealmConfigValues values) =>
        await RealmConfigGenerator.GenerateAsync(RealmFixturePaths.Template, values);

    private static RealmConfigValues SampleValues() => new(
        RealmId: 7,
        Port: 9400,
        IdentityConnectionString: "db;3306;forge;secret;realmd",
        WorldConnectionString: "db;3306;forge;secret;world",
        CharacterConnectionString: "db;3306;forge;secret;character");

    private static string ExpectedValueFor(string key, RealmConfigValues values) => key switch
    {
        "RealmID" => values.RealmId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "LoginDatabaseInfo" => Quote(values.IdentityConnectionString),
        "WorldDatabaseInfo" => Quote(values.WorldConnectionString),
        "CharacterDatabaseInfo" => Quote(values.CharacterConnectionString),
        "WorldServerPort" => values.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"'{key}' is not an overridden key"),
    };

    private static string Quote(string value) => $"\"{value}\"";

    private static string? OverriddenKeyOf(string line)
    {
        foreach (var key in new[] { "RealmID", "LoginDatabaseInfo", "WorldDatabaseInfo", "CharacterDatabaseInfo", "WorldServerPort" })
        {
            if (line.StartsWith(key + " ", StringComparison.Ordinal) ||
                line.StartsWith(key + "=", StringComparison.Ordinal) ||
                line.StartsWith(key + "  ", StringComparison.Ordinal))
            {
                return key;
            }
        }

        return null;
    }

    // Writes a full template around the given key line so all five keys are
    // present; the test then only varies the shape of that one value.
    private static async Task<string> WriteTemplateAsync(string key, string replacementLine)
    {
        var template = Path.Combine(Path.GetTempPath(), $"forge_test_template_{Guid.NewGuid():N}.conf");
        var lines = new List<string>
        {
            "RealmID = 1",
            "LoginDatabaseInfo = \"127.0.0.1;3306;mangos;mangos;realmd\"",
            "WorldDatabaseInfo = \"127.0.0.1;3306;mangos;mangos;mangos\"",
            "CharacterDatabaseInfo = \"127.0.0.1;3306;mangos;mangos;characters\"",
            "WorldServerPort = 8085",
        };
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(key + " ", StringComparison.Ordinal) ||
                lines[i].StartsWith(key + "=", StringComparison.Ordinal))
            {
                lines[i] = replacementLine;
            }
        }

        await File.WriteAllTextAsync(template, string.Join("\n", lines) + "\n");
        return template;
    }

    private static string[] SplitLines(string text) => text.Split('\n');
}
