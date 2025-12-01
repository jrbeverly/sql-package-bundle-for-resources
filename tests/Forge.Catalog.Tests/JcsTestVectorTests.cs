using System.Text.Json.Nodes;
using Xunit;

namespace Forge.Catalog.Tests;

// The published RFC 8785 test vectors, from the json-canonicalization spec
// repository. HLD.md Phase 2 acceptance: the implementation passes them.
public class JcsTestVectorTests
{
    [Theory]
    [InlineData("arrays")]
    [InlineData("french")]
    [InlineData("structures")]
    [InlineData("unicode")]
    [InlineData("values")]
    [InlineData("weird")]
    public void PublishedVectorCanonicalizesToExpectedOutput(string name)
    {
        var input = ReadVector(name, "input");
        var expected = ReadVector(name, "output");

        var canonical = Jcs.Canonicalize(JsonNode.Parse(input)!);

        Assert.Equal(expected, canonical);
    }

    [Fact]
    public void CanonicalizedOutputIsStableWhenReCanonicalized()
    {
        var canonical = Jcs.Canonicalize(JsonNode.Parse(ReadVector("structures", "input"))!);

        Assert.Equal(canonical, Jcs.Canonicalize(JsonNode.Parse(canonical)!));
    }

    [Fact]
    public void NumberSerializationMatchesTheAppendixBSamples()
    {
        Assert.Equal("1e+21", Jcs.Canonicalize(JsonNode.Parse("1e21")!));
        Assert.Equal("0.000001", Jcs.Canonicalize(JsonNode.Parse("0.000001")!));
        Assert.Equal("9.999999999999997e-7", Jcs.Canonicalize(JsonNode.Parse("9.999999999999997e-7")!));
        Assert.Equal("295147905179352830000", Jcs.Canonicalize(JsonNode.Parse("295147905179352830000")!));
        Assert.Equal("1424953923781206.2", Jcs.Canonicalize(JsonNode.Parse("1424953923781206.25")!));
        Assert.Equal("1.7976931348623157e+308", Jcs.Canonicalize(JsonNode.Parse("1.7976931348623157e+308")!));
        Assert.Equal("5e-324", Jcs.Canonicalize(JsonNode.Parse("5e-324")!));
        Assert.Equal("0", Jcs.Canonicalize(JsonNode.Parse("-0.0")!));
        Assert.Equal("9007199254740992", Jcs.Canonicalize(JsonNode.Parse("9007199254740992")!));
        Assert.Equal("-0.0000033333333333333333", Jcs.Canonicalize(JsonNode.Parse("-0.0000033333333333333333")!));
    }

    private static string ReadVector(string name, string kind)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "JcsVectors", $"{kind}-{name}.json");
        Assert.True(File.Exists(path), $"Missing test vector {path}.");
        return File.ReadAllText(path);
    }
}
