using System.Text.Json;
using Agent.Core.Approval;
using Agent.Core.Tools;
using Agent.Tools;
using Agent.Workspace;
using FluentAssertions;
using Moq;

namespace Agent.Tools.Tests;

public class ListFilesToolEnhancedTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalWorkspace _workspace;
    private readonly ToolContext _context;
    private readonly ListFilesTool _tool;

    public ListFilesToolEnhancedTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_listfiles_enhanced_" + Guid.NewGuid());
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
        try { Directory.Delete(_testRoot, recursive: true); } catch { }
    }

    [Fact]
    public async Task ExecuteAsync_WithPath_ReturnsRepoRootRelativePaths()
    {
        var srcDir = Path.Combine(_testRoot, "src");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "Program.cs"), "content");
        await File.WriteAllTextAsync(Path.Combine(srcDir, "Util.cs"), "content");

        var args = JsonDocument.Parse("""{"glob": "*.cs", "path": "src"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        var data = JsonSerializer.Serialize(result.Data);
        // Paths must be repo-root-relative, not searchRoot-relative
        data.Should().Contain("src/Program.cs");
        data.Should().Contain("src/Util.cs");
    }

    [Fact]
    public async Task ExecuteAsync_WithoutPath_ReturnsRepoRootRelativePaths()
    {
        var srcDir = Path.Combine(_testRoot, "src");
        Directory.CreateDirectory(srcDir);
        await File.WriteAllTextAsync(Path.Combine(srcDir, "App.cs"), "content");

        var args = JsonDocument.Parse("""{"glob": "**/*.cs"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        var data = JsonSerializer.Serialize(result.Data);
        data.Should().Contain("src/App.cs");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidPath_Fails()
    {
        var args = JsonDocument.Parse("""{"glob": "*.cs", "path": "nonexistent"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().Contain(d => d.Message.Contains("not found"));
    }

    [Fact]
    public async Task ExecuteAsync_SortByModified_ReturnsMostRecentFirst()
    {
        var srcDir = Path.Combine(_testRoot, "src");
        Directory.CreateDirectory(srcDir);

        var oldFile = Path.Combine(srcDir, "old.cs");
        await File.WriteAllTextAsync(oldFile, "old");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddHours(-2));

        var newFile = Path.Combine(srcDir, "new.cs");
        await File.WriteAllTextAsync(newFile, "new");
        File.SetLastWriteTimeUtc(newFile, DateTime.UtcNow);

        var args = JsonDocument.Parse("""{"glob": "**/*.cs", "sortBy": "modified"}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        var data = JsonSerializer.Serialize(result.Data);
        var newIdx = data.IndexOf("new.cs", StringComparison.Ordinal);
        var oldIdx = data.IndexOf("old.cs", StringComparison.Ordinal);
        newIdx.Should().BeLessThan(oldIdx, "newer file should appear first");
    }
}
