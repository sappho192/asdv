using System.Text.Json;
using Agent.Core.Tools;

namespace Agent.Tools;

public class FileEditTool : ITool
{
    public string Name => "FileEdit";
    public string Description => "Replace exact text in a file. The old_string must appear exactly once unless replace_all is true.";
    public ToolPolicy Policy => new() { RequiresApproval = true, Risk = RiskLevel.Medium };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "filePath": { "type": "string", "description": "Relative path to the file to edit" },
            "oldString": { "type": "string", "description": "The exact text to find and replace" },
            "newString": { "type": "string", "description": "The replacement text" },
            "replaceAll": { "type": "boolean", "description": "Replace all occurrences (default: false)" }
        },
        "required": ["filePath", "oldString", "newString"]
    }
    """;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var filePath = args.GetProperty("filePath").GetString()!;
        var oldString = args.GetProperty("oldString").GetString()!;
        var newString = args.GetProperty("newString").GetString()!;
        var replaceAll = args.TryGetProperty("replaceAll", out var ra) && ra.GetBoolean();

        var fullPath = ctx.Workspace.ResolvePath(filePath);
        if (fullPath == null)
        {
            return ToolResult.Failure($"Path traversal detected or path outside repo: {filePath}");
        }

        if (!File.Exists(fullPath))
        {
            return ToolResult.Failure($"File not found: {filePath}");
        }

        var content = await File.ReadAllTextAsync(fullPath, ct);

        var count = CountOccurrences(content, oldString);
        if (count == 0)
        {
            return ToolResult.Failure("old_string not found in file.");
        }

        if (count > 1 && !replaceAll)
        {
            return ToolResult.Failure(
                $"old_string found {count} times. Set replaceAll=true to replace all, or provide more context to make it unique.");
        }

        if (oldString == newString)
        {
            return ToolResult.Failure("old_string and new_string are identical. No change needed.");
        }

        var newContent = replaceAll
            ? content.Replace(oldString, newString)
            : ReplaceFirst(content, oldString, newString);

        await File.WriteAllTextAsync(fullPath, newContent, ct);

        var replacedCount = replaceAll ? count : 1;

        return ToolResult.Success(new
        {
            filePath,
            replacements = replacedCount,
            message = $"Replaced {replacedCount} occurrence(s) in {filePath}"
        });
    }

    private static int CountOccurrences(string text, string search)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(search, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += search.Length;
        }
        return count;
    }

    private static string ReplaceFirst(string text, string oldValue, string newValue)
    {
        var index = text.IndexOf(oldValue, StringComparison.Ordinal);
        if (index < 0) return text;
        return string.Concat(text.AsSpan(0, index), newValue, text.AsSpan(index + oldValue.Length));
    }
}
