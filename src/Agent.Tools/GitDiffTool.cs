using System.Diagnostics;
using System.Text.Json;
using Agent.Core.Tools;

namespace Agent.Tools;

public class GitDiffTool : ITool
{
    public string Name => "GitDiff";
    public string Description => "Get the git diff of changes in the repository";
    public ToolPolicy Policy => new() { IsReadOnly = true };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "staged": { "type": "boolean", "description": "Show staged changes only", "default": false },
            "file": { "type": "string", "description": "Specific file to diff" }
        }
    }
    """;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var staged = args.TryGetProperty("staged", out var s) && s.GetBoolean();
        var file = args.TryGetProperty("file", out var f) ? f.GetString() : null;

        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = ctx.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("diff");

        if (staged)
        {
            psi.ArgumentList.Add("--cached");
        }

        if (!string.IsNullOrEmpty(file))
        {
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add(file);
        }

        using var process = Process.Start(psi);
        if (process == null)
        {
            return ToolResult.Failure("Failed to start git process");
        }

        var stdout = await process.StandardOutput.ReadToEndAsync(ct);
        var stderr = await process.StandardError.ReadToEndAsync(ct);

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            return ToolResult.Failure($"git diff failed: {stderr}");
        }

        return ToolResult.Success(new
        {
            staged,
            file,
            hasDiff = !string.IsNullOrWhiteSpace(stdout),
            diff = stdout
        }, stdout);
    }
}
