using FluentAssertions;

namespace Agent.Tools.Tests;

public class EnvironmentDetectorTests : IDisposable
{
    private readonly string _testRoot;

    public EnvironmentDetectorTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_envdetect_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_testRoot, recursive: true); } catch { }
    }

    [Fact]
    public void Detect_NonGitDir_ReportsNotGitRepo()
    {
        var result = EnvironmentDetector.Detect(_testRoot);
        result.IsGitRepo.Should().BeFalse();
    }

    [Fact]
    public void Detect_GitDir_ReportsGitRepo()
    {
        Directory.CreateDirectory(Path.Combine(_testRoot, ".git"));
        var result = EnvironmentDetector.Detect(_testRoot);
        result.IsGitRepo.Should().BeTrue();
    }

    [Fact]
    public void Detect_GitFile_ReportsGitRepo()
    {
        // Simulates git worktree/submodule where .git is a file
        File.WriteAllText(Path.Combine(_testRoot, ".git"), "gitdir: /some/path/.git/worktrees/branch");
        var result = EnvironmentDetector.Detect(_testRoot);
        result.IsGitRepo.Should().BeTrue();
    }

    [Fact]
    public void Detect_ReportsOsDescription()
    {
        var result = EnvironmentDetector.Detect(_testRoot);
        result.OsDescription.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FormatForPrompt_NonGitRepo_ContainsWarning()
    {
        var env = new EnvironmentDetector.EnvironmentInfo(
            IsGitRepo: false, HasNode: false, HasPython: false,
            NodeVersion: null, PythonVersion: null, OsDescription: "Test OS");

        var prompt = EnvironmentDetector.FormatForPrompt(env);
        prompt.Should().Contain("NOT a git repository");
        prompt.Should().Contain("Do not use GitStatus");
    }

    [Fact]
    public void FormatForPrompt_WithRuntimes_IncludesVersions()
    {
        var env = new EnvironmentDetector.EnvironmentInfo(
            IsGitRepo: true, HasNode: true, HasPython: true,
            NodeVersion: "v20.0.0", PythonVersion: "Python 3.11.0", OsDescription: "Test OS");

        var prompt = EnvironmentDetector.FormatForPrompt(env);
        prompt.Should().Contain("Git repository detected");
        prompt.Should().Contain("Node.js available (v20.0.0)");
        prompt.Should().Contain("Python available (Python 3.11.0)");
    }
}
