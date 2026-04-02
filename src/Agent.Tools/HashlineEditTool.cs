using System.Text.Json;
using Agent.Core.Tools;
using Agent.Tools.Hashline;

namespace Agent.Tools;

public class HashlineEditTool : ITool
{
    public string Name => "HashlineEdit";
    public string Description => "Edit file lines using hashline references (LINE#HASH format from ReadFile output). Supports replace, append, and prepend operations with batch editing.";
    public ToolPolicy Policy => new() { RequiresApproval = true, Risk = RiskLevel.Medium };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "filePath": { "type": "string", "description": "Relative path to the file to edit" },
            "edits": {
                "type": "array",
                "description": "Array of edit operations",
                "items": {
                    "type": "object",
                    "properties": {
                        "op": { "type": "string", "enum": ["replace", "append", "prepend"], "description": "Edit operation" },
                        "pos": { "type": "string", "description": "Anchor in LINE#HASH format (e.g. 10#VK)" },
                        "end": { "type": "string", "description": "Range end anchor for replace (e.g. 15#XJ)" },
                        "lines": {
                            "description": "New content as string or array. null deletes lines (replace only).",
                            "oneOf": [
                                { "type": "string" },
                                { "type": "array", "items": { "type": "string" } },
                                { "type": "null" }
                            ]
                        }
                    },
                    "required": ["op", "lines"]
                }
            },
            "delete": { "type": "boolean", "description": "Delete file instead of editing" }
        },
        "required": ["filePath", "edits"]
    }
    """;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var filePath = args.GetProperty("filePath").GetString()!;
        var fullPath = ctx.Workspace.ResolvePath(filePath);

        if (fullPath == null)
            return ToolResult.Failure($"Path traversal detected or path outside repo: {filePath}");

        // Handle delete
        if (args.TryGetProperty("delete", out var delEl) && delEl.GetBoolean())
        {
            if (!File.Exists(fullPath))
                return ToolResult.Failure($"File not found: {filePath}");
            File.Delete(fullPath);
            return ToolResult.Success(new { filePath, deleted = true });
        }

        if (!args.TryGetProperty("edits", out var editsArray))
            return ToolResult.Failure("edits array is required");

        // Normalize edits from JSON
        HashlineEdit[] edits;
        try
        {
            edits = HashlineEditNormalization.NormalizeEdits(editsArray);
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"Invalid edit format: {ex.Message}");
        }

        // Read file (or start empty for new files)
        string rawContent;
        if (File.Exists(fullPath))
        {
            rawContent = await File.ReadAllTextAsync(fullPath, ct);
        }
        else
        {
            // New file — only append/prepend without anchors allowed
            rawContent = "";
        }

        // Canonicalize (preserve BOM/line endings)
        var envelope = FileTextCanonicalization.Canonicalize(rawContent);

        // Apply edits
        HashlineEditOperations.ApplyReport report;
        try
        {
            report = HashlineEditOperations.ApplyEditsWithReport(envelope.Content, edits);
        }
        catch (HashlineMismatchException ex)
        {
            return new ToolResult
            {
                Ok = false,
                Data = new { filePath, error = "hash_mismatch" },
                Diagnostics =
                [
                    new Diagnostic("HashMismatch", ex.Message),
                    new Diagnostic("Hint", "File has changed since last read. Re-read the file with ReadFile and use the updated LINE#HASH references.")
                ]
            };
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"Edit failed: {ex.Message}");
        }

        // Check for no-ops
        if (report.NoopEdits == edits.Length)
        {
            return new ToolResult
            {
                Ok = true,
                Data = new { filePath, noChange = true, message = "All edits were no-ops (content unchanged)" }
            };
        }

        // Restore original BOM/line endings and write
        var restored = FileTextCanonicalization.Restore(report.Content, envelope);

        // Ensure directory exists
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(fullPath, restored, ct);

        var resultData = new
        {
            filePath,
            editsApplied = edits.Length - report.NoopEdits,
            noopEdits = report.NoopEdits,
            deduplicatedEdits = report.DeduplicatedEdits,
            message = $"Applied {edits.Length - report.NoopEdits} edit(s) to {filePath}"
        };

        // Run post-edit validation
        var diagnostics = new List<Diagnostic>();
        var validation = await FileValidation.ValidateFileAsync(fullPath, ctx, ct);
        if (validation != null)
            diagnostics.Add(validation);

        return new ToolResult
        {
            Ok = true,
            Data = resultData,
            Diagnostics = diagnostics.Count > 0 ? diagnostics : null
        };
    }
}
