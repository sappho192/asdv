using System.Text.Json;
using Agent.Core.Tools;

namespace Agent.Tools;

public class WriteFileTool : ITool
{
    public string Name => "WriteFile";
    public string Description => "Create a new file or overwrite an existing file with the given content.";
    public ToolPolicy Policy => new() { RequiresApproval = true, Risk = RiskLevel.Medium };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "filePath": { "type": "string", "description": "Relative path to the file" },
            "content": { "type": "string", "description": "Full file content to write" },
            "overwrite": { "type": "boolean", "description": "Allow overwriting existing files (default: false)" }
        },
        "required": ["filePath", "content"]
    }
    """;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var filePath = args.GetProperty("filePath").GetString()!;
        var content = args.GetProperty("content").GetString()!;
        var overwrite = args.TryGetProperty("overwrite", out var ow) && ow.GetBoolean();

        var fullPath = ctx.Workspace.ResolvePath(filePath);
        if (fullPath == null)
            return ToolResult.Failure($"Path traversal detected or path outside repo: {filePath}");

        if (File.Exists(fullPath) && !overwrite)
        {
            return ToolResult.Failure(
                $"File already exists: {filePath}. Set overwrite=true to replace it, " +
                "or use HashlineEdit/FileEdit for targeted changes.");
        }

        // Ensure directory exists
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(fullPath, content, ct);

        var diagnostics = new List<Diagnostic>();
        var validation = await FileValidation.ValidateFileAsync(fullPath, ctx, ct);
        if (validation != null)
            diagnostics.Add(validation);

        return new ToolResult
        {
            Ok = true,
            Data = new
            {
                filePath,
                created = !File.Exists(fullPath) || !overwrite,
                bytes = content.Length,
                message = $"Wrote {content.Length} bytes to {filePath}"
            },
            Diagnostics = diagnostics.Count > 0 ? diagnostics : null
        };
    }
}
