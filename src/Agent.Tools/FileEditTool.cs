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
            return FailWithHint($"File not found: {filePath}",
                "Use WriteFile to create new files, or check the path with ListFiles.");
        }

        if (string.IsNullOrEmpty(oldString))
        {
            return FailWithHint("old_string must not be empty.",
                "To create a new file, use WriteFile. To insert content, use HashlineEdit with append/prepend.");
        }

        var content = await File.ReadAllTextAsync(fullPath, ct);

        var count = CountOccurrences(content, oldString);
        if (count == 0)
        {
            return FailWithHint("old_string not found in file.",
                "The file content may have changed. Re-read the file first. " +
                "For large replacements, prefer HashlineEdit (line-based) or WriteFile (full rewrite).");
        }

        if (count > 1 && !replaceAll)
        {
            return FailWithHint(
                $"old_string found {count} times. Set replaceAll=true to replace all, or provide more context to make it unique.",
                "Alternatively, use HashlineEdit with specific LINE#HASH references for precise editing.");
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

        // Post-edit validation
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
                replacements = replacedCount,
                message = $"Replaced {replacedCount} occurrence(s) in {filePath}"
            },
            Diagnostics = diagnostics.Count > 0 ? diagnostics : null
        };
    }

    private static ToolResult FailWithHint(string message, string hint)
    {
        return new ToolResult
        {
            Ok = false,
            Diagnostics =
            [
                new Diagnostic("Error", message),
                new Diagnostic("Hint", hint)
            ]
        };
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
