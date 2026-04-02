using System.Diagnostics;
using Agent.Core.Workspace;

namespace Agent.Workspace;

/// <summary>
/// IWorkspace implementation backed by a git worktree for isolated execution.
/// Creates a temporary worktree branch, works there, and supports merge back.
/// </summary>
public sealed class WorktreeWorkspace : IWorkspace, IDisposable
{
    private readonly string _mainRepoRoot;
    private readonly LocalWorkspace _inner;
    private bool _disposed;

    public string Root => _inner.Root;
    public string BranchName { get; }
    public string WorktreePath { get; }

    private WorktreeWorkspace(string mainRepoRoot, string worktreePath, string branchName)
    {
        _mainRepoRoot = mainRepoRoot;
        WorktreePath = worktreePath;
        BranchName = branchName;
        _inner = new LocalWorkspace(worktreePath);
    }

    public static async Task<WorktreeWorkspace> CreateAsync(
        string mainRepoRoot, string sessionId, CancellationToken ct = default)
    {
        var branchName = $"asdv/worktree-{sessionId}";
        var worktreePath = Path.Combine(mainRepoRoot, ".agent", "worktrees", sessionId);

        // Ensure parent directory exists
        var parentDir = Path.GetDirectoryName(worktreePath);
        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
            Directory.CreateDirectory(parentDir);

        // Create worktree
        var result = await RunGitAsync(mainRepoRoot,
            $"worktree add -b {branchName} {worktreePath}", ct);

        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Failed to create worktree: {result.Stderr}");

        return new WorktreeWorkspace(mainRepoRoot, worktreePath, branchName);
    }

    public string? ResolvePath(string relativePath) => _inner.ResolvePath(relativePath);
    public bool IsPathSafe(string fullPath) => _inner.IsPathSafe(fullPath);

    /// <summary>
    /// Get a diff of changes made in the worktree vs the main branch.
    /// </summary>
    public async Task<string> GetDiffAsync(CancellationToken ct = default)
    {
        var result = await RunGitAsync(WorktreePath, "diff HEAD", ct);
        return result.Stdout;
    }

    /// <summary>
    /// Attempt to merge worktree changes back to the original branch.
    /// Returns (success, message). On conflict, worktree is kept alive for manual resolution.
    /// </summary>
    public async Task<(bool Success, string Message)> MergeBackAsync(CancellationToken ct = default)
    {
        // First commit any uncommitted changes in worktree
        var statusResult = await RunGitAsync(WorktreePath, "status --porcelain", ct);
        if (!string.IsNullOrWhiteSpace(statusResult.Stdout))
        {
            await RunGitAsync(WorktreePath, "add -A", ct);
            await RunGitAsync(WorktreePath, "commit -m \"asdv: worktree changes\"", ct);
        }

        // Get current branch in main repo
        var branchResult = await RunGitAsync(_mainRepoRoot, "branch --show-current", ct);
        var mainBranch = branchResult.Stdout.Trim();

        // Merge worktree branch into main branch
        var mergeResult = await RunGitAsync(_mainRepoRoot, $"merge {BranchName}", ct);

        if (mergeResult.ExitCode == 0)
        {
            return (true, $"Successfully merged {BranchName} into {mainBranch}.");
        }

        // Merge conflict — abort and keep worktree
        await RunGitAsync(_mainRepoRoot, "merge --abort", ct);
        return (false,
            $"Merge conflict detected. Worktree preserved at: {WorktreePath}\n" +
            $"Branch: {BranchName}\n" +
            $"Resolve manually and merge, or discard with: git worktree remove {WorktreePath}");
    }

    /// <summary>
    /// Discard the worktree and its branch without merging.
    /// </summary>
    public async Task DiscardAsync(CancellationToken ct = default)
    {
        await RunGitAsync(_mainRepoRoot, $"worktree remove --force {WorktreePath}", ct);
        await RunGitAsync(_mainRepoRoot, $"branch -D {BranchName}", ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Don't auto-delete — let the user decide via MergeBack or Discard
    }

    private static async Task<GitResult> RunGitAsync(string workingDir, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
            return new GitResult(1, "", "Failed to start git process");

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return new GitResult(process.ExitCode, stdout, stderr);
    }

    private sealed record GitResult(int ExitCode, string Stdout, string Stderr);
}
