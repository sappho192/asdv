using System.Text.Json;
using Agent.Core.Approval;
using Agent.Core.Tools;
using Agent.Tools;
using Agent.Workspace;
using FluentAssertions;
using Moq;

namespace Agent.Tools.Tests;

public class SearchTextToolTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalWorkspace _workspace;
    private readonly ToolContext _context;
    private readonly SearchTextTool _tool;

    public SearchTextToolTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_searchtext_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        _workspace = new LocalWorkspace(_testRoot);
        _context = new ToolContext
        {
            RepoRoot = _testRoot,
            Workspace = _workspace,
            ApprovalService = Mock.Of<IApprovalService>()
        };
        _tool = new SearchTextTool();
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
    public async Task ExecuteAsync_InvalidRegex_ReturnsFailure()
    {
        using var _ = new PathScope(string.Empty);
        var args = JsonDocument.Parse("""{"query":"["}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics![0].Message.Should().Contain("Invalid regex pattern");
    }

    [Fact]
    public async Task ExecuteAsync_ManualSearch_ReturnsMatches()
    {
        using var _ = new PathScope(string.Empty);
        var file1 = Path.Combine(_testRoot, "file1.txt");
        var file2 = Path.Combine(_testRoot, "file2.txt");
        await File.WriteAllTextAsync(file1, "hello needle world");
        await File.WriteAllTextAsync(file2, "nothing here");

        var args = JsonDocument.Parse("""{"query":"needle","maxResults":10}""").RootElement;

        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        var dataJson = JsonSerializer.Serialize(result.Data);
        dataJson.Should().Contain("file1.txt");
        dataJson.Should().Contain("needle");
    }

    private sealed class PathScope : IDisposable
    {
        private readonly string? _originalPath;

        public PathScope(string newPath)
        {
            _originalPath = Environment.GetEnvironmentVariable("PATH");
            Environment.SetEnvironmentVariable("PATH", newPath);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("PATH", _originalPath);
        }
    }
}
