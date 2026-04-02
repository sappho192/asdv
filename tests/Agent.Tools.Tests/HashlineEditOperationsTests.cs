using Agent.Tools.Hashline;
using FluentAssertions;

namespace Agent.Tools.Tests;

public class HashlineEditOperationsTests
{
    private static string Hash(int line, string content) => HashlineComputation.ComputeLineHash(line, content);

    [Fact]
    public void ApplyEdits_ReplaceSingleLine_Works()
    {
        var content = "line1\nline2\nline3";
        var h2 = Hash(2, "line2");
        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Replace($"2#{h2}", null, ["replaced"])
        };

        var result = HashlineEditOperations.ApplyEdits(content, edits);
        result.Should().Be("line1\nreplaced\nline3");
    }

    [Fact]
    public void ApplyEdits_ReplaceRange_Works()
    {
        var content = "line1\nline2\nline3\nline4";
        var h2 = Hash(2, "line2");
        var h3 = Hash(3, "line3");
        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Replace($"2#{h2}", $"3#{h3}", ["new2", "new3"])
        };

        var result = HashlineEditOperations.ApplyEdits(content, edits);
        result.Should().Be("line1\nnew2\nnew3\nline4");
    }

    [Fact]
    public void ApplyEdits_AppendAfterLine_Works()
    {
        var content = "line1\nline2\nline3";
        var h2 = Hash(2, "line2");
        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Append($"2#{h2}", ["inserted"])
        };

        var result = HashlineEditOperations.ApplyEdits(content, edits);
        result.Should().Be("line1\nline2\ninserted\nline3");
    }

    [Fact]
    public void ApplyEdits_PrependBeforeLine_Works()
    {
        var content = "line1\nline2\nline3";
        var h2 = Hash(2, "line2");
        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Prepend($"2#{h2}", ["inserted"])
        };

        var result = HashlineEditOperations.ApplyEdits(content, edits);
        result.Should().Be("line1\ninserted\nline2\nline3");
    }

    [Fact]
    public void ApplyEdits_AppendToEnd_Works()
    {
        var content = "line1\nline2";
        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Append(null, ["line3", "line4"])
        };

        var result = HashlineEditOperations.ApplyEdits(content, edits);
        result.Should().Be("line1\nline2\nline3\nline4");
    }

    [Fact]
    public void ApplyEdits_PrependToStart_Works()
    {
        var content = "line1\nline2";
        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Prepend(null, ["line0"])
        };

        var result = HashlineEditOperations.ApplyEdits(content, edits);
        result.Should().Be("line0\nline1\nline2");
    }

    [Fact]
    public void ApplyEdits_BatchEdits_AppliedCorrectly()
    {
        var content = "aaa\nbbb\nccc\nddd";
        var h1 = Hash(1, "aaa");
        var h4 = Hash(4, "ddd");
        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Replace($"1#{h1}", null, ["AAA"]),
            new HashlineEdit.Replace($"4#{h4}", null, ["DDD"])
        };

        var result = HashlineEditOperations.ApplyEdits(content, edits);
        result.Should().Be("AAA\nbbb\nccc\nDDD");
    }

    [Fact]
    public void ApplyEdits_DuplicateEdits_Deduplicated()
    {
        var content = "line1\nline2";
        var h1 = Hash(1, "line1");
        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Replace($"1#{h1}", null, ["replaced"]),
            new HashlineEdit.Replace($"1#{h1}", null, ["replaced"]) // duplicate
        };

        var report = HashlineEditOperations.ApplyEditsWithReport(content, edits);
        report.DeduplicatedEdits.Should().Be(1);
        report.Content.Should().Be("replaced\nline2");
    }

    [Fact]
    public void ApplyEdits_NoopEdit_Detected()
    {
        var content = "line1\nline2";
        var h1 = Hash(1, "line1");
        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Replace($"1#{h1}", null, ["line1"]) // no change
        };

        var report = HashlineEditOperations.ApplyEditsWithReport(content, edits);
        report.NoopEdits.Should().Be(1);
    }

    [Fact]
    public void ApplyEdits_HashMismatch_ThrowsException()
    {
        var content = "line1\nline2";
        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Replace("1#ZZ", null, ["replaced"]) // wrong hash
        };

        var act = () => HashlineEditOperations.ApplyEdits(content, edits);
        act.Should().Throw<HashlineMismatchException>();
    }

    [Fact]
    public void DetectOverlappingRanges_RangeVsRange_Detected()
    {
        _ = "a\nb\nc\nd\ne";
        var h1 = Hash(1, "a"); var h3 = Hash(3, "c");
        var h2 = Hash(2, "b"); var h4 = Hash(4, "d");

        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Replace($"1#{h1}", $"3#{h3}", ["x"]),
            new HashlineEdit.Replace($"2#{h2}", $"4#{h4}", ["y"])
        };

        var error = HashlineEditOperations.DetectOverlappingRanges(edits);
        error.Should().NotBeNull();
        error.Should().Contain("Overlapping");
    }

    [Fact]
    public void DetectOverlappingRanges_SingleEditInsideRange_Detected()
    {
        _ = "a\nb\nc\nd\ne";
        var h1 = Hash(1, "a"); var h4 = Hash(4, "d");
        var h2 = Hash(2, "b");

        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Replace($"1#{h1}", $"4#{h4}", ["x"]),
            new HashlineEdit.Replace($"2#{h2}", null, ["y"]) // inside range
        };

        var error = HashlineEditOperations.DetectOverlappingRanges(edits);
        error.Should().NotBeNull();
        error.Should().Contain("falls inside range");
    }

    [Fact]
    public void DetectOverlappingRanges_AppendInsideRange_Detected()
    {
        _ = "a\nb\nc\nd\ne";
        var h1 = Hash(1, "a"); var h4 = Hash(4, "d");
        var h3 = Hash(3, "c");

        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Replace($"1#{h1}", $"4#{h4}", ["x"]),
            new HashlineEdit.Append($"3#{h3}", ["inserted"]) // inside range
        };

        var error = HashlineEditOperations.DetectOverlappingRanges(edits);
        error.Should().NotBeNull();
        error.Should().Contain("falls inside range");
    }

    [Fact]
    public void DetectOverlappingRanges_NonOverlapping_ReturnsNull()
    {
        _ = "a\nb\nc\nd\ne";
        var h1 = Hash(1, "a"); var h2 = Hash(2, "b");
        var h4 = Hash(4, "d"); var h5 = Hash(5, "e");

        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Replace($"1#{h1}", $"2#{h2}", ["x"]),
            new HashlineEdit.Replace($"4#{h4}", $"5#{h5}", ["y"])
        };

        var error = HashlineEditOperations.DetectOverlappingRanges(edits);
        error.Should().BeNull();
    }

    [Fact]
    public void ApplyEdits_EmptyEdits_ReturnsUnchanged()
    {
        var content = "line1\nline2";
        var result = HashlineEditOperations.ApplyEdits(content, []);
        result.Should().Be(content);
    }

    [Fact]
    public void ApplyEdits_DeleteLines_WithNullContent()
    {
        var content = "line1\nline2\nline3";
        var h2 = Hash(2, "line2");
        var edits = new HashlineEdit[]
        {
            new HashlineEdit.Replace($"2#{h2}", null, []) // delete
        };

        var result = HashlineEditOperations.ApplyEdits(content, edits);
        result.Should().Be("line1\nline3");
    }
}
