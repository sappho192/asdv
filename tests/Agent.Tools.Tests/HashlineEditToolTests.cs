using System.Text.Json;
using Agent.Core.Approval;
using Agent.Core.Tools;
using Agent.Tools.Hashline;
using Agent.Workspace;
using FluentAssertions;
using Moq;

namespace Agent.Tools.Tests;

public class HashlineEditToolTests : IDisposable
{
    private readonly string _testRoot;
    private readonly ToolContext _context;
    private readonly HashlineEditTool _tool;

    public HashlineEditToolTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_hashlineedit_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        _context = new ToolContext
        {
            RepoRoot = _testRoot,
            Workspace = new LocalWorkspace(_testRoot),
            ApprovalService = Mock.Of<IApprovalService>()
        };
        _tool = new HashlineEditTool();
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, recursive: true); } catch { }
    }

    private static string H(int line, string content) => HashlineComputation.ComputeLineHash(line, content);

    [Fact]
    public async Task ExecuteAsync_ReplaceSingleLine_Works()
    {
        var file = Path.Combine(_testRoot, "test.txt");
        await File.WriteAllTextAsync(file, "aaa\nbbb\nccc");
        var h2 = H(2, "bbb");

        var args = JsonDocument.Parse($$"""
        {
            "filePath": "test.txt",
            "edits": [
                { "op": "replace", "pos": "2#{{h2}}", "lines": "BBB" }
            ]
        }
        """).RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        (await File.ReadAllTextAsync(file)).Should().Be("aaa\nBBB\nccc");
    }

    [Fact]
    public async Task ExecuteAsync_AppendLine_Works()
    {
        var file = Path.Combine(_testRoot, "test.txt");
        await File.WriteAllTextAsync(file, "aaa\nbbb\nccc");
        var h2 = H(2, "bbb");

        var args = JsonDocument.Parse($$"""
        {
            "filePath": "test.txt",
            "edits": [
                { "op": "append", "pos": "2#{{h2}}", "lines": "inserted" }
            ]
        }
        """).RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        (await File.ReadAllTextAsync(file)).Should().Be("aaa\nbbb\ninserted\nccc");
    }

    [Fact]
    public async Task ExecuteAsync_HashMismatch_ReturnsHint()
    {
        var file = Path.Combine(_testRoot, "test.txt");
        await File.WriteAllTextAsync(file, "aaa\nbbb\nccc");

        var args = JsonDocument.Parse("""
        {
            "filePath": "test.txt",
            "edits": [
                { "op": "replace", "pos": "2#ZZ", "lines": "BBB" }
            ]
        }
        """).RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Code == "HashMismatch");
        result.Diagnostics.Should().Contain(d => d.Code == "Hint" && d.Message.Contains("Re-read"));
    }

    [Fact]
    public async Task ExecuteAsync_DeleteFile_Works()
    {
        var file = Path.Combine(_testRoot, "todelete.txt");
        await File.WriteAllTextAsync(file, "content");

        var args = JsonDocument.Parse("""
        {
            "filePath": "todelete.txt",
            "edits": [],
            "delete": true
        }
        """).RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        File.Exists(file).Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_FileNotFound_Fails()
    {
        var args = JsonDocument.Parse("""
        {
            "filePath": "nonexistent.txt",
            "edits": [
                { "op": "replace", "pos": "1#ZZ", "lines": "x" }
            ]
        }
        """).RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_InvalidOp_Fails()
    {
        var file = Path.Combine(_testRoot, "test.txt");
        await File.WriteAllTextAsync(file, "aaa");

        var args = JsonDocument.Parse("""
        {
            "filePath": "test.txt",
            "edits": [
                { "op": "invalid", "pos": "1#ZZ", "lines": "x" }
            ]
        }
        """).RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("unsupported op"));
    }

    [Fact]
    public void Tool_HasCorrectMetadata()
    {
        _tool.Name.Should().Be("HashlineEdit");
        _tool.Policy.RequiresApproval.Should().BeTrue();
        _tool.Policy.Risk.Should().Be(RiskLevel.Medium);
    }
}
