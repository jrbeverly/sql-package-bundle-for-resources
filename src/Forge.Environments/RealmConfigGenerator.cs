using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Forge.Contracts;

namespace Forge.Environments;

// Generates a realm's server config by overriding the realm-specific keys of
// the operator-supplied template and preserving every other line byte for
// byte, comments included (DEPLOY.md, Configuration). A template missing one
// of the keys is refused rather than patched up, because an absent key means
// the template is not the file this layer thinks it is.
public static class RealmConfigGenerator
{
    // The realm-specific keys of the core's server config (mangosd.conf).
    // Everything else belongs to the core's defaults and is preserved.
    private static readonly (string Key, bool IsInteger)[] Overrides =
    [
        ("RealmID", true),
        ("LoginDatabaseInfo", false),
        ("WorldDatabaseInfo", false),
        ("CharacterDatabaseInfo", false),
        ("WorldServerPort", true),
    ];

    public static async Task<RealmResult<string>> GenerateAsync(
        string templatePath, RealmConfigValues values, CancellationToken cancellationToken = default)
    {
        string text;
        try
        {
            text = await File.ReadAllTextAsync(templatePath, cancellationToken);
        }
        catch (Exception e)
        {
            return RealmResult<string>.Failure(
                RealmErrorCode.E_CONFIG_FORMAT_UNEXPECTED, $"template '{templatePath}' could not be read: {e.Message}");
        }

        // A key to be overridden that is absent from the template is a
        // refusal, before anything is written (DEPLOY.md, Configuration).
        foreach (var (key, _) in Overrides)
        {
            if (!Regex.IsMatch(text, $"(?m)^\\s*{key}\\s*=", RegexOptions.None, TimeSpan.FromSeconds(1)))
            {
                return RealmResult<string>.Failure(
                    RealmErrorCode.E_CONFIG_KEY_MISSING, $"template '{templatePath}' has no '{key}' key to override");
            }
        }

        // Split on '\n' so a line's original content, including any '\r', is
        // preserved exactly; only the overridden keys' values are replaced.
        var lines = text.Split('\n');
        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var hadCarriageReturn = line.EndsWith('\r');
            var content = hadCarriageReturn ? line[..^1] : line;

            var replacementResult = ReplaceValue(content, values, templatePath);
            if (replacementResult.ErrorCode is not null)
            {
                return RealmResult<string>.Failure(replacementResult.ErrorCode.Value, replacementResult.Message!);
            }

            builder.Append(replacementResult.Value!).Append(hadCarriageReturn ? "\r" : null).Append(i < lines.Length - 1 ? "\n" : null);
        }

        return RealmResult<string>.Success(builder.ToString());
    }

    private static RealmResult<string> ReplaceValue(string line, RealmConfigValues values, string templatePath)
    {
        foreach (var (key, isInteger) in Overrides)
        {
            var match = Regex.Match(line, $"^(\\s*{key}\\s*=\\s*)(.*)$", RegexOptions.None, TimeSpan.FromSeconds(1));
            if (!match.Success)
            {
                continue;
            }

            var shapeResult = ValidateExistingValue(key, match.Groups[2].Value, isInteger, templatePath);
            if (shapeResult.ErrorCode is not null)
            {
                return RealmResult<string>.Failure(shapeResult.ErrorCode.Value, shapeResult.Message!);
            }

            // Keep the template's own prefix (indentation, key spelling, and
            // spacing around '='); only the value changes.
            return RealmResult<string>.Success(match.Groups[1].Value + ValueFor(key, values));
        }

        return RealmResult<string>.Success(line);
    }

    private static RealmResult<bool> ValidateExistingValue(
        string key, string existingValue, bool isInteger, string templatePath)
    {
        if (isInteger)
        {
            if (!int.TryParse(existingValue.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return RealmResult<bool>.Failure(
                    RealmErrorCode.E_CONFIG_FORMAT_UNEXPECTED,
                    $"template '{templatePath}': key '{key}' does not hold an integer value");
            }

            return RealmResult<bool>.Success(true);
        }

        // The *DatabaseInfo values are "host;port;user;password;database"
        // (DEPLOY.md, Configuration): five semicolon-separated fields inside
        // double quotes.
        var trimmed = existingValue.Trim();
        if (trimmed.Length < 2 || !trimmed.StartsWith('"') || !trimmed.EndsWith('"') ||
            trimmed[1..^1].Split(';').Length != 5)
        {
            return RealmResult<bool>.Failure(
                RealmErrorCode.E_CONFIG_FORMAT_UNEXPECTED,
                $"template '{templatePath}': key '{key}' does not hold a \"host;port;user;password;database\" value");
        }

        return RealmResult<bool>.Success(true);
    }

    private static string ValueFor(string key, RealmConfigValues values) => key switch
    {
        "RealmID" => values.RealmId.ToString(CultureInfo.InvariantCulture),
        "LoginDatabaseInfo" => Quote(values.IdentityConnectionString),
        "WorldDatabaseInfo" => Quote(values.WorldConnectionString),
        "CharacterDatabaseInfo" => Quote(values.CharacterConnectionString),
        "WorldServerPort" => values.Port.ToString(CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException($"key '{key}' is not a realm-specific override"),
    };

    private static string Quote(string value) => $"\"{value}\"";
}
