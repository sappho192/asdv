using System.Diagnostics;
using System.Text.Json;
using Agent.Core.Tools;

namespace Agent.Tools;

public class GitStatusTool : ITool
{
    public string Name => "GitStatus";
    public string Description => "Get the current git status of the repository";
    public ToolPolicy Policy => new() { IsReadOnly = true };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {}
    }
    """;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = ctx.RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("status");
        psi.ArgumentList.Add("--porcelain");
        psi.ArgumentList.Add("-b");

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
            return ToolResult.Failure($"git status failed: {stderr}");
        }

        var lines = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var branch = "";
        var changes = new List<object>();

        foreach (var line in lines)
        {
            if (line.StartsWith("## "))
            {
                branch = line[3..].Split("...")[0];
            }
            else if (line.Length >= 2)
            {
                var status = line[..2];
                var file = line[3..];
                changes.Add(new { status, file });
            }
        }

        return ToolResult.Success(new
        {
            branch,
            changes,
            clean = changes.Count == 0
        }, stdout);
    }
}
