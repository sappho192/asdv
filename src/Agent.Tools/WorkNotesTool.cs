using System.Text.Json;
using Agent.Core.Tools;

namespace Agent.Tools;

public class WorkNotesTool : ITool
{
    public string Name => "WorkNotes";

    public string Description =>
        "Read and write structured work notes that persist across session turns. " +
        "Use to store plans, findings, progress, and TODOs. " +
        "Notes are injected into the system prompt each turn so you can reference them.";

    public ToolPolicy Policy => new()
    {
        IsReadOnly = false,
        RequiresApproval = false,
        Risk = RiskLevel.Low
    };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "action": {
                "type": "string",
                "enum": ["set", "get", "list", "clear"],
                "description": "Action to perform: set (store key-value), get (retrieve by key), list (all notes), clear (remove key or all)"
            },
            "key": {
                "type": "string",
                "description": "Note key (required for set, get, clear-single)"
            },
            "value": {
                "type": "string",
                "description": "Note value (required for set)"
            }
        },
        "required": ["action"]
    }
    """;

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var action = args.TryGetProperty("action", out var actionProp)
            ? actionProp.GetString()?.ToLowerInvariant()
            : null;

        if (string.IsNullOrEmpty(action))
            return Task.FromResult(ToolResult.Failure("action is required"));

        var key = args.TryGetProperty("key", out var keyProp) ? keyProp.GetString() : null;
        var value = args.TryGetProperty("value", out var valueProp) ? valueProp.GetString() : null;

        var notes = ctx.SessionNotes;
        if (notes == null)
            return Task.FromResult(ToolResult.Failure("Work notes are not available in this context"));

        return Task.FromResult(action switch
        {
            "set" => SetNote(notes, key, value),
            "get" => GetNote(notes, key),
            "list" => ListNotes(notes),
            "clear" => ClearNote(notes, key),
            _ => ToolResult.Failure($"Unknown action: {action}. Use set, get, list, or clear.")
        });
    }

    private static ToolResult SetNote(Dictionary<string, string> notes, string? key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
            return ToolResult.Failure("key is required for set");
        if (value == null)
            return ToolResult.Failure("value is required for set");

        notes[key] = value;
        return ToolResult.Success(new { action = "set", key, stored = true });
    }

    private static ToolResult GetNote(Dictionary<string, string> notes, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return ToolResult.Failure("key is required for get");

        if (notes.TryGetValue(key, out var value))
            return ToolResult.Success(new { action = "get", key, value });

        return ToolResult.Success(new { action = "get", key, found = false });
    }

    private static ToolResult ListNotes(Dictionary<string, string> notes)
    {
        if (notes.Count == 0)
            return ToolResult.Success(new { action = "list", count = 0, notes = new Dictionary<string, string>() });

        return ToolResult.Success(new { action = "list", count = notes.Count, notes });
    }

    private static ToolResult ClearNote(Dictionary<string, string> notes, string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            var count = notes.Count;
            notes.Clear();
            return ToolResult.Success(new { action = "clear", cleared = count });
        }

        var removed = notes.Remove(key);
        return ToolResult.Success(new { action = "clear", key, removed });
    }
}
