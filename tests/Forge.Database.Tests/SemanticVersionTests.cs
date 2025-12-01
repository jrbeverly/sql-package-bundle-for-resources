using Forge.Contracts;
using Xunit;

namespace Forge.Database.Tests;

// SemanticVersion lives in Forge.Contracts but is exercised here because the
// package upgrader is its only consumer; these tests need no MySQL.
public class SemanticVersionTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0", "1.1.0")]
    [InlineData("1.9.9", "1.10.0")]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0")]
    [InlineData("1.0.0-alpha", "1.0.0-beta")]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.2")]
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]
    [InlineData("1.0.0-2", "1.0.0-10")]
    [InlineData("1.0.0-2", "1.0.0-alpha")]
    [InlineData("1.0.0-beta.2", "1.0.0-beta.11")]
    [InlineData("1.0.0-alpha", "1.0.0+build")]
    public void LaterVersionComparesHigher(string earlier, string later)
    {
        Assert.True(Parse(later).CompareTo(Parse(earlier)) > 0, $"{later} should be higher than {earlier}");
        Assert.True(Parse(earlier).CompareTo(Parse(later)) < 0, $"{earlier} should be lower than {later}");
    }

    [Theory]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0.0+abc", "1.0.0")]
    [InlineData("1.0.0+abc", "1.0.0+def")]
    [InlineData("1.2.3-beta.1", "1.2.3-beta.1")]
    public void EqualVersionsCompareEqual(string left, string right)
    {
        Assert.Equal(0, Parse(left).CompareTo(Parse(right)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1.0.0.0")]
    [InlineData("not-a-version")]
    [InlineData("1.0.0-")]
    [InlineData("1.0.0+")]
    public void InvalidVersionsDoNotParse(string value)
    {
        Assert.Null(SemanticVersion.Parse(value));
    }

    private static SemanticVersion Parse(string value) => SemanticVersion.Parse(value)!;
}
