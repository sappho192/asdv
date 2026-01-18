using System.Text.Json;
using Agent.Core.Approval;
using Agent.Core.Tools;
using Agent.Tools;
using Agent.Workspace;
using FluentAssertions;
using Moq;

namespace Agent.Tools.Tests;

public class ApplyPatchToolTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalWorkspace _workspace;
    private readonly ToolContext _context;
    private readonly ApplyPatchTool _tool;

    public ApplyPatchToolTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_applypatch_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        _workspace = new LocalWorkspace(_testRoot);
        _context = new ToolContext
        {
            RepoRoot = _testRoot,
            Workspace = _workspace,
            ApprovalService = Mock.Of<IApprovalService>()
        };
        _tool = new ApplyPatchTool();
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
    public async Task ExecuteAsync_NewFilePatch_CreatesFile()
    {
        // Arrange
        var patch = """
        --- a/new.txt
        +++ b/new.txt
        @@ -0,0 +1,2 @@
        +hello
        +world
        """;
        var args = JsonDocument.Parse($$"""{"patch": {{JsonSerializer.Serialize(patch)}}}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeTrue();
        var filePath = Path.Combine(_testRoot, "new.txt");
        File.Exists(filePath).Should().BeTrue();
        var contents = await File.ReadAllLinesAsync(filePath);
        contents.Should().Equal("hello", "world");
    }

    [Fact]
    public async Task ExecuteAsync_PathTraversalPatch_ReturnsFailure()
    {
        // Arrange
        var patch = """
        --- a/../evil.txt
        +++ b/../evil.txt
        @@ -0,0 +1,1 @@
        +nope
        """;
        var args = JsonDocument.Parse($$"""{"patch": {{JsonSerializer.Serialize(patch)}}}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics![0].Message.Should().Contain("All patches failed");
    }

    [Fact]
    public async Task ExecuteAsync_PartialApply_ReturnsDiagnostics()
    {
        // Arrange
        var goodPath = Path.Combine(_testRoot, "good.txt");
        await File.WriteAllTextAsync(goodPath, "one");

        var patch = """
        --- a/good.txt
        +++ b/good.txt
        @@ -1,1 +1,2 @@
        -one
        +one
        +two
        --- a/../evil.txt
        +++ b/../evil.txt
        @@ -0,0 +1,1 @@
        +nope
        """;
        var args = JsonDocument.Parse($$"""{"patch": {{JsonSerializer.Serialize(patch)}}}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeTrue();
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics!.Any(d => d.Code == "PartialApply").Should().BeTrue();

        var updated = await File.ReadAllLinesAsync(goodPath);
        updated.Should().Equal("one", "two");

        var dataJson = JsonSerializer.Serialize(result.Data);
        dataJson.Should().Contain("good.txt");
        dataJson.Should().Contain("failedPatches");
    }
}
