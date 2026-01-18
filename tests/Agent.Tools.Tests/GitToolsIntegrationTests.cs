using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Agent.Core.Approval;
using Agent.Core.Tools;
using Agent.Tools;
using Agent.Workspace;
using FluentAssertions;
using Moq;

namespace Agent.Tools.Tests;

public class GitToolsIntegrationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly LocalWorkspace _workspace;
    private readonly ToolContext _context;
    private readonly GitStatusTool _statusTool;
    private readonly GitDiffTool _diffTool;
    private readonly bool _gitAvailable;
    private readonly string? _gitPath;

    public GitToolsIntegrationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_gittools_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        _workspace = new LocalWorkspace(_testRoot);
        _context = new ToolContext
        {
            RepoRoot = _testRoot,
            Workspace = _workspace,
            ApprovalService = Mock.Of<IApprovalService>()
        };
        _statusTool = new GitStatusTool();
        _diffTool = new GitDiffTool();

        _gitPath = ResolveGitPath();
        _gitAvailable = _gitPath != null && IsGitAvailable(_gitPath);
        if (_gitAvailable)
        {
            RunGit("init");
            RunGit("config", "user.email", "test@example.com");
            RunGit("config", "user.name", "Test User");
        }
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
    public async Task GitStatus_CleanRepo_ReturnsClean()
    {
        if (!_gitAvailable) return;

        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await _statusTool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        var dataJson = JsonSerializer.Serialize(result.Data);
        dataJson.Should().Contain("\"clean\":true");
        dataJson.Should().Contain("\"changes\"");
    }

    [Fact]
    public async Task GitStatus_UntrackedFile_ReturnsChanges()
    {
        if (!_gitAvailable) return;

        await File.WriteAllTextAsync(Path.Combine(_testRoot, "new.txt"), "content");
        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await _statusTool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        var dataJson = JsonSerializer.Serialize(result.Data);
        dataJson.Should().Contain("\"clean\":false");
        dataJson.Should().Contain("new.txt");
        dataJson.Should().Contain("??");
    }

    [Fact]
    public async Task GitDiff_UnstagedChanges_ReturnsDiff()
    {
        if (!_gitAvailable) return;

        var filePath = Path.Combine(_testRoot, "file.txt");
        await File.WriteAllTextAsync(filePath, "one\n");
        RunGit("add", "file.txt");
        RunGit("commit", "-m", "init");

        await File.WriteAllTextAsync(filePath, "one\ntwo\n");
        var args = JsonDocument.Parse("""{}""").RootElement;

        var result = await _diffTool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.Stdout.Should().Contain("+two");
        var dataJson = JsonSerializer.Serialize(result.Data);
        dataJson.Should().Contain("\"hasDiff\":true");
    }

    [Fact]
    public async Task GitDiff_StagedChanges_ReturnsDiff()
    {
        if (!_gitAvailable) return;

        var filePath = Path.Combine(_testRoot, "file.txt");
        await File.WriteAllTextAsync(filePath, "one\n");
        RunGit("add", "file.txt");
        RunGit("commit", "-m", "init");

        await File.WriteAllTextAsync(filePath, "one\ntwo\n");
        RunGit("add", "file.txt");

        var args = JsonDocument.Parse("""{"staged": true}""").RootElement;
        var result = await _diffTool.ExecuteAsync(args, _context, CancellationToken.None);

        result.Ok.Should().BeTrue();
        result.Stdout.Should().Contain("+two");
        var dataJson = JsonSerializer.Serialize(result.Data);
        dataJson.Should().Contain("\"staged\":true");
    }

    private static string? ResolveGitPath()
    {
        var candidates = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new[] { "git.exe", "git.cmd", "git.bat", "git" }
            : new[] { "git" };

        foreach (var candidate in candidates)
        {
            var found = FindOnPath(candidate);
            if (found != null)
                return found;
        }

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv))
            return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static bool IsGitAvailable(string gitPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = gitPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }
            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void RunGit(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _gitPath ?? "git",
            WorkingDirectory = _testRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start git process");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {stderr}{stdout}");
        }
    }
}
