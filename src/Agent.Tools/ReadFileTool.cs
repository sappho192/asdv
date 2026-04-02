using System.Text.Json;
using Agent.Core.Tools;
using Agent.Tools.Hashline;

namespace Agent.Tools;

public class ReadFileTool : ITool
{
    public string Name => "ReadFile";
    public string Description => "Read contents of a file with hashline format (LINE#HASH|content). Use the LINE#HASH references with HashlineEdit for precise editing.";
    public ToolPolicy Policy => new() { IsReadOnly = true, IsConcurrencySafe = true };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "path": { "type": "string", "description": "Relative path to the file" },
            "startLine": { "type": "integer", "description": "Start line (1-indexed)" },
            "endLine": { "type": "integer", "description": "End line (1-indexed)" }
        },
        "required": ["path"]
    }
    """;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var path = args.GetProperty("path").GetString()!;
        var fullPath = ctx.Workspace.ResolvePath(path);

        if (fullPath == null)
        {
            return ToolResult.Failure($"Path traversal detected or path outside repo: {path}");
        }

        if (!File.Exists(fullPath))
        {
            return ToolResult.Failure($"File not found: {path}");
        }

        var lines = await File.ReadAllLinesAsync(fullPath, ct);

        int startLine = args.TryGetProperty("startLine", out var s) ? s.GetInt32() : 1;
        int endLine = args.TryGetProperty("endLine", out var e) ? e.GetInt32() : lines.Length;

        startLine = Math.Max(1, startLine);
        endLine = Math.Min(lines.Length, endLine);

        // Build hashline-formatted output
        var hashlineOutput = new System.Text.StringBuilder();
        for (int i = startLine; i <= endLine; i++)
        {
            if (i > startLine) hashlineOutput.Append('\n');
            hashlineOutput.Append(HashlineComputation.FormatHashLine(i, lines[i - 1]));
        }

        return ToolResult.Success(new
        {
            path,
            startLine,
            endLine,
            totalLines = lines.Length,
            content = hashlineOutput.ToString()
        });
    }
}
