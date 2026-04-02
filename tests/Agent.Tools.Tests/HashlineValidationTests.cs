using Agent.Tools.Hashline;
using FluentAssertions;

namespace Agent.Tools.Tests;

public class HashlineValidationTests
{
    [Theory]
    [InlineData("10#VK", 10, "VK")]
    [InlineData("1#ZZ", 1, "ZZ")]
    [InlineData("999#HH", 999, "HH")]
    public void ParseLineRef_ValidRef_ReturnsCorrectValues(string refStr, int expectedLine, string expectedHash)
    {
        var result = HashlineValidation.ParseLineRef(refStr);
        result.Line.Should().Be(expectedLine);
        result.Hash.Should().Be(expectedHash);
    }

    [Fact]
    public void ParseLineRef_InvalidFormat_Throws()
    {
        var act = () => HashlineValidation.ParseLineRef("invalid");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Invalid line reference*");
    }

    [Fact]
    public void ParseLineRef_NonNumericLine_ThrowsWithHint()
    {
        var act = () => HashlineValidation.ParseLineRef("abc#VK");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a line number*");
    }

    [Theory]
    [InlineData(">>> 10#VK|content here", "10#VK")]
    [InlineData("+ 5#XJ|added line", "5#XJ")]
    [InlineData("  10 # VK | content", "10#VK")]
    public void NormalizeLineRef_StripsMarkersAndNormalizes(string input, string expected)
    {
        HashlineValidation.NormalizeLineRef(input).Should().Be(expected);
    }

    [Fact]
    public void ValidateLineRefs_ValidRefs_DoesNotThrow()
    {
        var lines = new[] { "const x = 1;", "const y = 2;" };
        var hash1 = HashlineComputation.ComputeLineHash(1, lines[0]);
        var hash2 = HashlineComputation.ComputeLineHash(2, lines[1]);

        var act = () => HashlineValidation.ValidateLineRefs(lines, [$"1#{hash1}", $"2#{hash2}"]);
        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateLineRefs_MismatchedHash_ThrowsWithContext()
    {
        var lines = new[] { "const x = 1;", "const y = 2;" };

        var act = () => HashlineValidation.ValidateLineRefs(lines, ["1#ZZ"]);
        act.Should().Throw<HashlineMismatchException>()
            .WithMessage("*changed since last read*");
    }

    [Fact]
    public void ValidateLineRefs_OutOfBounds_Throws()
    {
        var lines = new[] { "line1" };

        var act = () => HashlineValidation.ValidateLineRefs(lines, ["99#VK"]);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*out of bounds*");
    }

    [Fact]
    public void HashlineMismatchException_ProvidesRemaps()
    {
        var lines = new[] { "const x = 1;", "const y = 2;" };
        var ex = new HashlineMismatchException([(1, "ZZ")], lines);

        ex.Remaps.Should().ContainKey("1#ZZ");
        var actualHash = HashlineComputation.ComputeLineHash(1, lines[0]);
        ex.Remaps["1#ZZ"].Should().Be($"1#{actualHash}");
    }
}
