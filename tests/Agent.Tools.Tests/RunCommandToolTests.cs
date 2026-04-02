using System.Runtime.InteropServices;
using System.Text.Json;
using Agent.Core.Approval;
using Agent.Core.Tools;
using Agent.Tools;
using Agent.Workspace;
using FluentAssertions;
using Moq;

namespace Agent.Tools.Tests;

public class RunCommandToolTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalWorkspace _workspace;
    private readonly ToolContext _context;
    private readonly RunCommandTool _tool;

    public RunCommandToolTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_runcommand_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        _workspace = new LocalWorkspace(_testRoot);
        _context = new ToolContext
        {
            RepoRoot = _testRoot,
            Workspace = _workspace,
            ApprovalService = Mock.Of<IApprovalService>()
        };
        _tool = new RunCommandTool();
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
    public async Task ExecuteAsync_InvalidCwd_ReturnsFailure()
    {
        // Arrange
        var args = JsonDocument.Parse("""{"command":"dotnet --version","cwd":"../.."}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics![0].Message.Should().Contain("Invalid working directory");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidCommand_ReturnsFailure()
    {
        // Arrange
        var args = JsonDocument.Parse("""{"command":"this-command-should-not-exist-123"}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_CommandTimeout_ReturnsFailure()
    {
        // Arrange
        var args = JsonDocument.Parse("""{"command":"sleep 2","timeoutSec":1}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics![0].Message.Should().Contain("timed out");
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulCommand_ReturnsStdout()
    {
        // Arrange
        var args = JsonDocument.Parse("""{"command":"echo hello"}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeTrue();
        result.Stdout.Should().NotBeNull();
        result.Stdout!.ToLowerInvariant().Should().Contain("hello");
    }

    [Fact]
    public async Task ExecuteAsync_ShellOverride_UsesSpecifiedShell()
    {
        // Arrange
        var args = JsonDocument.Parse("""{"command":"echo hello","shell":"bash"}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeTrue();
        result.Stdout.Should().NotBeNull();
        result.Stdout!.ToLowerInvariant().Should().Contain("hello");
    }
}
