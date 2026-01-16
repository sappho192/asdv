using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Agent.Core.Tools;

namespace Agent.Tools;

public class ApplyPatchTool : ITool
{
    public string Name => "ApplyPatch";
    public string Description => "Apply a unified diff patch to files in the repository";
    public ToolPolicy Policy => new() { RequiresApproval = true, Risk = RiskLevel.Medium };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "patch": { "type": "string", "description": "Unified diff format patch" }
        },
        "required": ["patch"]
    }
    """;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var patch = args.GetProperty("patch").GetString()!;

        // Try git apply first
        var gitResult = await TryGitApplyAsync(patch, ctx.RepoRoot, ct);
        if (gitResult.Ok)
        {
            return gitResult;
        }

        // Fallback to manual patch application
        return await ApplyPatchManuallyAsync(patch, ctx, ct);
    }

    private static async Task<ToolResult> TryGitApplyAsync(string patch, string root, CancellationToken ct)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, patch, ct);

            // Check if patch can be applied
            var checkResult = await RunGitAsync(root, ["apply", "--check", tempFile], ct);
            if (checkResult.exitCode != 0)
            {
                return ToolResult.Failure($"Patch check failed: {checkResult.stderr}");
            }

            // Actually apply the patch
            var applyResult = await RunGitAsync(root, ["apply", tempFile], ct);
            if (applyResult.exitCode != 0)
            {
                return ToolResult.Failure($"Patch application failed: {applyResult.stderr}");
            }

            return ToolResult.Success(new { method = "git apply", applied = true });
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"Git apply error: {ex.Message}");
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunGitAsync(
        string workDir, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        return (process.ExitCode, stdout, stderr);
    }

    private async Task<ToolResult> ApplyPatchManuallyAsync(string patch, ToolContext ctx, CancellationToken ct)
    {
        var filePatches = ParseUnifiedDiff(patch);
        if (filePatches.Count == 0)
        {
            return ToolResult.Failure("No valid patches found in input");
        }

        var appliedFiles = new List<string>();
        var failedPatches = new List<object>();

        foreach (var filePatch in filePatches)
        {
            var targetPath = filePatch.NewPath ?? filePatch.OldPath;
            if (string.IsNullOrEmpty(targetPath))
            {
                failedPatches.Add(new { file = "(unknown)", reason = "No file path found" });
                continue;
            }

            // Clean path (remove a/ or b/ prefix)
            targetPath = CleanDiffPath(targetPath);

            var fullPath = ctx.Workspace.ResolvePath(targetPath);
            if (fullPath == null)
            {
                failedPatches.Add(new { file = targetPath, reason = "Path traversal detected" });
                continue;
            }

            try
            {
                var result = await ApplyHunksToFileAsync(fullPath, filePatch.Hunks, ct);
                if (result.success)
                {
                    appliedFiles.Add(targetPath);
                }
                else
                {
                    failedPatches.Add(new { file = targetPath, reason = result.error });
                }
            }
            catch (Exception ex)
            {
                failedPatches.Add(new { file = targetPath, reason = ex.Message });
            }
        }

        if (failedPatches.Count > 0 && appliedFiles.Count == 0)
        {
            return ToolResult.Failure(
                $"All patches failed to apply",
                JsonSerializer.Serialize(failedPatches));
        }

        if (failedPatches.Count > 0)
        {
            return new ToolResult
            {
                Ok = true,
                Data = new { method = "manual", appliedFiles, failedPatches },
                Diagnostics = [new Diagnostic("PartialApply", $"{failedPatches.Count} patches failed")]
            };
        }

        return ToolResult.Success(new { method = "manual", appliedFiles });
    }

    private static string CleanDiffPath(string path)
    {
        if (path.StartsWith("a/") || path.StartsWith("b/"))
        {
            return path[2..];
        }
        return path;
    }

    private static List<FilePatch> ParseUnifiedDiff(string patch)
    {
        var result = new List<FilePatch>();
        var lines = patch.Split('\n');

        FilePatch? currentFile = null;
        Hunk? currentHunk = null;

        var hunkHeaderRegex = new Regex(@"^@@\s*-(\d+)(?:,(\d+))?\s*\+(\d+)(?:,(\d+))?\s*@@");

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (line.StartsWith("--- "))
            {
                if (currentFile != null)
                {
                    if (currentHunk != null)
                    {
                        currentFile.Hunks.Add(currentHunk);
                    }
                    result.Add(currentFile);
                }

                currentFile = new FilePatch { OldPath = line[4..].Trim() };
                currentHunk = null;
            }
            else if (line.StartsWith("+++ ") && currentFile != null)
            {
                currentFile.NewPath = line[4..].Trim();
            }
            else if (line.StartsWith("@@") && currentFile != null)
            {
                if (currentHunk != null)
                {
                    currentFile.Hunks.Add(currentHunk);
                }

                var match = hunkHeaderRegex.Match(line);
                if (match.Success)
                {
                    currentHunk = new Hunk
                    {
                        OldStart = int.Parse(match.Groups[1].Value),
                        OldCount = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 1,
                        NewStart = int.Parse(match.Groups[3].Value),
                        NewCount = match.Groups[4].Success ? int.Parse(match.Groups[4].Value) : 1
                    };
                }
            }
            else if (currentHunk != null)
            {
                if (line.StartsWith("+") || line.StartsWith("-") || line.StartsWith(" ") || line == "")
                {
                    currentHunk.Lines.Add(line);
                }
            }
        }

        if (currentFile != null)
        {
            if (currentHunk != null)
            {
                currentFile.Hunks.Add(currentHunk);
            }
            result.Add(currentFile);
        }

        return result;
    }

    private static async Task<(bool success, string? error)> ApplyHunksToFileAsync(
        string path, List<Hunk> hunks, CancellationToken ct)
    {
        List<string> lines;

        if (File.Exists(path))
        {
            lines = (await File.ReadAllLinesAsync(path, ct)).ToList();
        }
        else
        {
            // New file
            lines = [];
        }

        // Apply hunks in reverse order to maintain line numbers
        var sortedHunks = hunks.OrderByDescending(h => h.OldStart).ToList();

        foreach (var hunk in sortedHunks)
        {
            var applyResult = ApplyHunk(lines, hunk);
            if (!applyResult.success)
            {
                return (false, applyResult.error);
            }
            lines = applyResult.lines!;
        }

        // Ensure directory exists
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllLinesAsync(path, lines, ct);
        return (true, null);
    }

    private static (bool success, string? error, List<string>? lines) ApplyHunk(List<string> lines, Hunk hunk)
    {
        var result = new List<string>(lines);
        var insertIndex = Math.Max(0, hunk.OldStart - 1);

        // For new files, just add all the + lines
        if (lines.Count == 0 && hunk.OldStart <= 1)
        {
            foreach (var line in hunk.Lines)
            {
                if (line.StartsWith("+"))
                {
                    result.Add(line[1..]);
                }
            }
            return (true, null, result);
        }

        // Remove old lines and add new lines
        var removeCount = 0;
        var linesToAdd = new List<string>();

        foreach (var line in hunk.Lines)
        {
            if (line.StartsWith("-"))
            {
                removeCount++;
            }
            else if (line.StartsWith("+"))
            {
                linesToAdd.Add(line.Length > 1 ? line[1..] : "");
            }
            else if (line.StartsWith(" ") || line == "")
            {
                linesToAdd.Add(line.Length > 1 ? line[1..] : "");
            }
        }

        // Verify context if possible
        if (insertIndex < result.Count && removeCount > 0)
        {
            // Remove old lines
            var endIndex = Math.Min(insertIndex + removeCount, result.Count);
            result.RemoveRange(insertIndex, endIndex - insertIndex);
        }

        // Insert new lines
        result.InsertRange(insertIndex, linesToAdd);

        return (true, null, result);
    }

    private class FilePatch
    {
        public string? OldPath { get; set; }
        public string? NewPath { get; set; }
        public List<Hunk> Hunks { get; } = [];
    }

    private class Hunk
    {
        public int OldStart { get; set; }
        public int OldCount { get; set; }
        public int NewStart { get; set; }
        public int NewCount { get; set; }
        public List<string> Lines { get; } = [];
    }
}
