using System.Text.Json;
using Agent.Core.Approval;
using Agent.Core.Tools;
using Agent.Workspace;
using FluentAssertions;
using Moq;

namespace Agent.Tools.Tests;

public class WriteFileToolTests : IDisposable
{
    private readonly string _testRoot;
    private readonly ToolContext _context;
    private readonly WriteFileTool _tool;

    public WriteFileToolTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_writefile_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        _context = new ToolContext
        {
            RepoRoot = _testRoot,
            Workspace = new LocalWorkspace(_testRoot),
            ApprovalService = Mock.Of<IApprovalService>()
        };
        _tool = new WriteFileTool();
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task ExecuteAsync_NewFile_CreatesSuccessfully()
    {
        var args = JsonDocument.Parse("""{"filePath": "new.txt", "content": "hello world"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        var content = await File.ReadAllTextAsync(Path.Combine(_testRoot, "new.txt"));
        content.Should().Be("hello world");
    }

    [Fact]
    public async Task ExecuteAsync_ExistingFileWithoutOverwrite_Fails()
    {
        var file = Path.Combine(_testRoot, "existing.txt");
        await File.WriteAllTextAsync(file, "original");
        var args = JsonDocument.Parse("""{"filePath": "existing.txt", "content": "new content"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("already exists"));
        (await File.ReadAllTextAsync(file)).Should().Be("original");
    }

    [Fact]
    public async Task ExecuteAsync_ExistingFileWithOverwrite_Succeeds()
    {
        var file = Path.Combine(_testRoot, "existing.txt");
        await File.WriteAllTextAsync(file, "original");
        var args = JsonDocument.Parse("""{"filePath": "existing.txt", "content": "new content", "overwrite": true}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        (await File.ReadAllTextAsync(file)).Should().Be("new content");
    }

    [Fact]
    public async Task ExecuteAsync_CreatesDirectories()
    {
        var args = JsonDocument.Parse("""{"filePath": "sub/dir/file.txt", "content": "nested"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        File.Exists(Path.Combine(_testRoot, "sub", "dir", "file.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_PathTraversal_Fails()
    {
        var args = JsonDocument.Parse("""{"filePath": "../escape.txt", "content": "bad"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_JsonFile_ValidatesContent()
    {
        var args = JsonDocument.Parse("""{"filePath": "data.json", "content": "{invalid json"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue(); // Write succeeds but with validation warning
        result.Diagnostics.Should().Contain(d => d.Code == "ValidationWarning");
    }

    [Fact]
    public void Tool_HasCorrectMetadata()
    {
        _tool.Name.Should().Be("WriteFile");
        _tool.Policy.RequiresApproval.Should().BeTrue();
        _tool.Policy.Risk.Should().Be(RiskLevel.Medium);
    }
}
