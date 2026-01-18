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
        var args = JsonDocument.Parse("""{"exe":"dotnet","cwd":"../.."}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics![0].Message.Should().Contain("Invalid working directory");
    }

    [Fact]
    public async Task ExecuteAsync_InvalidExecutable_ReturnsFailure()
    {
        // Arrange
        var args = JsonDocument.Parse("""{"exe":"this-command-should-not-exist-123"}""").RootElement;

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeFalse();
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics![0].Message.Should().Contain("Failed to start process");
    }

    [Fact]
    public async Task ExecuteAsync_CommandTimeout_ReturnsFailure()
    {
        // Arrange
        JsonElement args;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            args = JsonDocument.Parse("""{"exe":"cmd","args":["/c","for /L %i in (1,1,200000000) do @rem"],"timeoutSec":1}""")
                .RootElement;
        }
        else
        {
            var shell = ResolveShellPath();
            args = JsonDocument.Parse($"{{\"exe\":\"{shell}\",\"args\":[\"-c\",\"sleep 2\"],\"timeoutSec\":1}}")
                .RootElement;
        }

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
        JsonElement args;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            args = JsonDocument.Parse("""{"exe":"cmd","args":["/c","echo","hello"]}""").RootElement;
        }
        else
        {
            var shell = ResolveShellPath();
            args = JsonDocument.Parse($"{{\"exe\":\"{shell}\",\"args\":[\"-c\",\"echo hello\"]}}").RootElement;
        }

        // Act
        var result = await _tool.ExecuteAsync(args, _context, CancellationToken.None);

        // Assert
        result.Ok.Should().BeTrue();
        result.Stdout.Should().NotBeNull();
        result.Stdout!.ToLowerInvariant().Should().Contain("hello");
    }

    private static string ResolveShellPath()
    {
        if (File.Exists("/bin/sh"))
            return "/bin/sh";

        if (File.Exists("/usr/bin/sh"))
            return "/usr/bin/sh";

        return "sh";
    }
}
