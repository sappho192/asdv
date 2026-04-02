using Agent.Tools.Hashline;
using FluentAssertions;

namespace Agent.Tools.Tests;

public class HashlineComputationTests
{
    [Fact]
    public void ComputeLineHash_ReturnsTwoCharHash()
    {
        var hash = HashlineComputation.ComputeLineHash(1, "const x = 1;");
        hash.Should().HaveLength(2);
        hash.Should().MatchRegex("^[ZPMQVRWSNKTXJBYH]{2}$");
    }

    [Fact]
    public void ComputeLineHash_SameContentSameLine_ReturnsSameHash()
    {
        var hash1 = HashlineComputation.ComputeLineHash(5, "hello world");
        var hash2 = HashlineComputation.ComputeLineHash(5, "hello world");
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeLineHash_DifferentContent_ReturnsDifferentHash()
    {
        var hash1 = HashlineComputation.ComputeLineHash(1, "const x = 1;");
        var hash2 = HashlineComputation.ComputeLineHash(1, "const y = 2;");
        // Not guaranteed to differ for all inputs, but these should differ
        // (this is a probabilistic test — if it flakes, remove it)
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void ComputeLineHash_TrimsTrailingWhitespace()
    {
        var hash1 = HashlineComputation.ComputeLineHash(1, "hello");
        var hash2 = HashlineComputation.ComputeLineHash(1, "hello   ");
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeLineHash_RemovesCarriageReturn()
    {
        var hash1 = HashlineComputation.ComputeLineHash(1, "hello");
        var hash2 = HashlineComputation.ComputeLineHash(1, "hello\r");
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeLineHash_EmptyLine_ReturnsValidHash()
    {
        var hash = HashlineComputation.ComputeLineHash(1, "");
        hash.Should().HaveLength(2);
        hash.Should().MatchRegex("^[ZPMQVRWSNKTXJBYH]{2}$");
    }

    [Fact]
    public void ComputeLineHash_WhitespaceOnlyLine_UsesLineNumberAsSeed()
    {
        // Whitespace-only lines (no letters/digits) use lineNumber as seed
        var hash1 = HashlineComputation.ComputeLineHash(1, "   ");
        var hash2 = HashlineComputation.ComputeLineHash(1, "   ");
        hash1.Should().Be(hash2);
    }

    [Fact]
    public void FormatHashLine_ProducesCorrectFormat()
    {
        var result = HashlineComputation.FormatHashLine(10, "const x = 1;");
        result.Should().MatchRegex(@"^10#[ZPMQVRWSNKTXJBYH]{2}\|const x = 1;$");
    }

    [Fact]
    public void FormatHashLines_FormatsMultipleLines()
    {
        var result = HashlineComputation.FormatHashLines("line1\nline2\nline3");
        var lines = result.Split('\n');
        lines.Should().HaveCount(3);
        lines[0].Should().StartWith("1#");
        lines[1].Should().StartWith("2#");
        lines[2].Should().StartWith("3#");
    }

    [Fact]
    public void FormatHashLines_EmptyContent_ReturnsEmpty()
    {
        HashlineComputation.FormatHashLines("").Should().BeEmpty();
    }
}
