using System.Text.Json;
using Agent.Core.Approval;
using Agent.Core.Tools;
using Agent.Tools;
using Agent.Workspace;
using FluentAssertions;
using Moq;

namespace Agent.Tools.Tests;

public class ListFilesToolTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalWorkspace _workspace;
    private readonly ToolContext _context;
    private readonly ListFilesTool _tool;

    public ListFilesToolTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_listfiles_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        _workspace = new LocalWorkspace(_testRoot);
        _context = new ToolContext
        {
            RepoRoot = _testRoot,
            Workspace = _workspace,
            ApprovalService = Mock.Of<IApprovalService>()
        };
        _tool = new ListFilesTool();
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
    public async Task ExecuteAsync_MatchingFiles_ReturnsFiles()
    {
        // Arrange
        var srcDir = Path.Combine(_testRoot, "src");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "file1.cs"), "content");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "file2.cs"), "content");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "file.txt"), "content");

        var args = JsonDocument.Parse("""{"glob": "**/*.cs"}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeTrue();
        var data = JsonSerializer.Serialize(result.Data);
        data.Should().Contain("file1.cs");
        data.Should().Contain("file2.cs");
        data.Should().NotContain("file.txt");
    }

    [Fact]
    public async Task ExecuteAsync_NoMatchingFiles_ReturnsEmptyList()
    {
        // Arrange
        var args = JsonDocument.Parse("""{"glob": "**/*.xyz"}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeTrue();
        var data = JsonSerializer.Serialize(result.Data);
        data.Should().Contain("\"count\":0");
    }

    [Fact]
    public void Tool_HasCorrectMetadata()
    {
        _tool.Name.Should().Be("ListFiles");
        _tool.Description.Should().NotBeEmpty();
        _tool.Policy.IsReadOnly.Should().BeTrue();
    }
}
