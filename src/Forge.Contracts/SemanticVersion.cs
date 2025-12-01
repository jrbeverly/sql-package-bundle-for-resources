namespace Forge.Contracts;

// metadata.version is Semantic Versioning 2.0.0 (SQL.md, Versions And
// Constraints). Precedence is §11: major, minor, and patch numerically, then
// the pre-release identifiers; build metadata is ignored (§10). The numeric
// parts stay as strings — the grammar forbids leading zeros, so a
// length-then-ordinal comparison is the numeric comparison, for arbitrarily
// long versions.
public sealed record SemanticVersion(string Major, string Minor, string Patch, string? PreRelease) : IComparable<SemanticVersion>
{
    public static SemanticVersion? Parse(string value)
    {
        var core = value;
        var buildSeparator = core.IndexOf('+');
        if (buildSeparator >= 0)
        {
            // The grammar requires at least one build identifier.
            if (buildSeparator == core.Length - 1)
            {
                return null;
            }

            core = core[..buildSeparator];
        }

        string? preRelease = null;
        var preSeparator = core.IndexOf('-');
        if (preSeparator >= 0)
        {
            // The grammar requires at least one pre-release identifier.
            if (preSeparator == core.Length - 1)
            {
                return null;
            }

            preRelease = core[(preSeparator + 1)..];
            core = core[..preSeparator];
        }

        var parts = core.Split('.');
        if (parts.Length != 3 || parts.Any(p => p.Length == 0 || !p.All(char.IsAsciiDigit)))
        {
            return null;
        }

        return new SemanticVersion(parts[0], parts[1], parts[2], preRelease);
    }

    public int CompareTo(SemanticVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var byMajor = CompareNumeric(Major, other.Major);
        if (byMajor != 0)
        {
            return byMajor;
        }

        var byMinor = CompareNumeric(Minor, other.Minor);
        if (byMinor != 0)
        {
            return byMinor;
        }

        var byPatch = CompareNumeric(Patch, other.Patch);
        if (byPatch != 0)
        {
            return byPatch;
        }

        // §11: a version with a pre-release has lower precedence than the
        // same version without one.
        if (PreRelease is null && other.PreRelease is null)
        {
            return 0;
        }

        if (PreRelease is null)
        {
            return 1;
        }

        if (other.PreRelease is null)
        {
            return -1;
        }

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    private static int CompareNumeric(string left, string right)
    {
        // No leading zeros in the grammar (only "0" itself), so the longer
        // digit string is the larger number.
        if (left.Length != right.Length)
        {
            return left.Length < right.Length ? -1 : 1;
        }

        return string.CompareOrdinal(left, right);
    }

    private static int ComparePreRelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var shared = Math.Min(leftParts.Length, rightParts.Length);
        for (var i = 0; i < shared; i++)
        {
            var byIdentifier = ComparePreReleaseIdentifier(leftParts[i], rightParts[i]);
            if (byIdentifier != 0)
            {
                return byIdentifier;
            }
        }

        // §11: a shorter set of identifiers is lower when it is a prefix of
        // the longer set.
        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static int ComparePreReleaseIdentifier(string left, string right)
    {
        var leftNumeric = left.All(char.IsAsciiDigit);
        var rightNumeric = right.All(char.IsAsciiDigit);
        if (leftNumeric && rightNumeric)
        {
            return CompareNumeric(left, right);
        }

        // §11: numeric identifiers rank below alphanumeric ones.
        if (leftNumeric)
        {
            return -1;
        }

        if (rightNumeric)
        {
            return 1;
        }

        return string.CompareOrdinal(left, right);
    }
}
