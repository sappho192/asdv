using System.Text.Json;
using Agent.Core.Approval;
using Agent.Core.Tools;
using Agent.Tools;
using Agent.Workspace;
using FluentAssertions;
using Moq;

namespace Agent.Tools.Tests;

public class ReadFileToolTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalWorkspace _workspace;
    private readonly ToolContext _context;
    private readonly ReadFileTool _tool;

    public ReadFileToolTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_tool_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        _workspace = new LocalWorkspace(_testRoot);
        _context = new ToolContext
        {
            RepoRoot = _testRoot,
            Workspace = _workspace,
            ApprovalService = Mock.Of<IApprovalService>()
        };
        _tool = new ReadFileTool();
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_testRoot, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExistingFile_ReturnsContent()
    {
        // Arrange
        var testFile = Path.Combine(_testRoot, "test.txt");
        await File.WriteAllTextAsync(testFile, "line1\nline2\nline3");
        var args = JsonDocument.Parse("""{"path": "test.txt"}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeTrue();
        result.Data.Should().NotBeNull();

        var data = JsonSerializer.Serialize(result.Data);
        data.Should().Contain("line1");
        data.Should().Contain("line2");
        data.Should().Contain("line3");
    }

    [Fact]
    public async Task ExecuteAsync_NonExistentFile_ReturnsFailure()
    {
        // Arrange
        var args = JsonDocument.Parse("""{"path": "nonexistent.txt"}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithLineRange_ReturnsPartialContent()
    {
        // Arrange
        var testFile = Path.Combine(_testRoot, "multiline.txt");
        await File.WriteAllTextAsync(testFile, "line1\nline2\nline3\nline4\nline5");
        var args = JsonDocument.Parse("""{"path": "multiline.txt", "startLine": 2, "endLine": 4}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeTrue();
        var data = JsonSerializer.Serialize(result.Data);
        data.Should().Contain("line2");
        data.Should().Contain("line3");
        data.Should().Contain("line4");
        data.Should().NotContain("line1");
        data.Should().NotContain("line5");
    }

    [Fact]
    public async Task ExecuteAsync_PathTraversal_ReturnsFailure()
    {
        // Arrange
        var args = JsonDocument.Parse("""{"path": "../../../etc/passwd"}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeFalse();
    }

    [Fact]
    public void Tool_HasCorrectMetadata()
    {
        _tool.Name.Should().Be("ReadFile");
        _tool.Description.Should().NotBeEmpty();
        _tool.Policy.IsReadOnly.Should().BeTrue();
        _tool.InputSchema.Should().Contain("path");
    }
}
