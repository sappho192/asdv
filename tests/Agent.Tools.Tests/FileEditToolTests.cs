using System.Text.Json;
using Agent.Core.Approval;
using Agent.Core.Tools;
using Agent.Tools;
using Agent.Workspace;
using FluentAssertions;
using Moq;

namespace Agent.Tools.Tests;

public class FileEditToolTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalWorkspace _workspace;
    private readonly ToolContext _context;
    private readonly FileEditTool _tool;

    public FileEditToolTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_fileedit_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        _workspace = new LocalWorkspace(_testRoot);
        _context = new ToolContext
        {
            RepoRoot = _testRoot,
            Workspace = _workspace,
            ApprovalService = Mock.Of<IApprovalService>()
        };
        _tool = new FileEditTool();
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task ExecuteAsync_UniqueMatch_ReplacesSuccessfully()
    {
        var file = Path.Combine(_testRoot, "test.cs");
        await File.WriteAllTextAsync(file, "public class Foo { }");
        var args = JsonDocument.Parse("""{"filePath": "test.cs", "oldString": "Foo", "newString": "Bar"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        var content = await File.ReadAllTextAsync(file);
        content.Should().Be("public class Bar { }");
    }

    [Fact]
    public async Task ExecuteAsync_MultipleMatches_FailsWithoutReplaceAll()
    {
        var file = Path.Combine(_testRoot, "test.cs");
        await File.WriteAllTextAsync(file, "foo bar foo baz foo");
        var args = JsonDocument.Parse("""{"filePath": "test.cs", "oldString": "foo", "newString": "qux"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("3 times"));
    }

    [Fact]
    public async Task ExecuteAsync_MultipleMatches_ReplacesAllWhenFlagged()
    {
        var file = Path.Combine(_testRoot, "test.cs");
        await File.WriteAllTextAsync(file, "foo bar foo baz foo");
        var args = JsonDocument.Parse("""{"filePath": "test.cs", "oldString": "foo", "newString": "qux", "replaceAll": true}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        var content = await File.ReadAllTextAsync(file);
        content.Should().Be("qux bar qux baz qux");
    }

    [Fact]
    public async Task ExecuteAsync_OldStringNotFound_Fails()
    {
        var file = Path.Combine(_testRoot, "test.cs");
        await File.WriteAllTextAsync(file, "hello world");
        var args = JsonDocument.Parse("""{"filePath": "test.cs", "oldString": "notfound", "newString": "x"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("not found"));
    }

    [Fact]
    public async Task ExecuteAsync_EmptyOldString_Fails()
    {
        var file = Path.Combine(_testRoot, "test.cs");
        await File.WriteAllTextAsync(file, "hello world");
        var args = JsonDocument.Parse("""{"filePath": "test.cs", "oldString": "", "newString": "x"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("must not be empty"));
    }

    [Fact]
    public async Task ExecuteAsync_SameOldAndNew_Fails()
    {
        var file = Path.Combine(_testRoot, "test.cs");
        await File.WriteAllTextAsync(file, "hello world");
        var args = JsonDocument.Parse("""{"filePath": "test.cs", "oldString": "hello", "newString": "hello"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("identical"));
    }

    [Fact]
    public async Task ExecuteAsync_FileNotFound_Fails()
    {
        var args = JsonDocument.Parse("""{"filePath": "nonexistent.cs", "oldString": "a", "newString": "b"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("not found"));
    }

    [Fact]
    public async Task ExecuteAsync_PathTraversal_Fails()
    {
        var args = JsonDocument.Parse("""{"filePath": "../etc/passwd", "oldString": "a", "newString": "b"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("traversal"));
    }

    [Fact]
    public void Tool_HasCorrectMetadata()
    {
        _tool.Name.Should().Be("FileEdit");
        _tool.Policy.RequiresApproval.Should().BeTrue();
        _tool.Policy.IsReadOnly.Should().BeFalse();
    }
}
