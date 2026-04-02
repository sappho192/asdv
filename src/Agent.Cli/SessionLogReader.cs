using System.Text.Json;
using Agent.Core.Messages;
using Agent.Core.Tools;

namespace Agent.Cli;

public enum ResumeMode
{
    Full,
    Summary,
    LastN
}

public sealed record SessionSnapshot(List<ChatMessage> Messages, Dictionary<string, string> Notes);

public static class SessionLogReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static SessionSnapshot LoadSession(
        string sessionPath, ResumeMode mode, int lastN, Action<string>? warn = null)
    {
        var (allMessages, notes) = LoadMessagesAndNotes(sessionPath, warn);

        var messages = mode switch
        {
            ResumeMode.Full => allMessages,
            ResumeMode.LastN => TakeLastTurns(allMessages, lastN),
            ResumeMode.Summary => GenerateSummaryMessages(allMessages),
            _ => allMessages
        };

        return new SessionSnapshot(messages, notes);
    }

    public static List<ChatMessage> LoadMessages(
        string sessionPath, ResumeMode mode, int lastN, Action<string>? warn = null)
    {
        return LoadSession(sessionPath, mode, lastN, warn).Messages;
    }

    public static List<ChatMessage> LoadMessages(string sessionPath, Action<string>? warn = null)
    {
        return LoadMessagesAndNotes(sessionPath, warn).Messages;
    }

    private static (List<ChatMessage> Messages, Dictionary<string, string> Notes) LoadMessagesAndNotes(
        string sessionPath, Action<string>? warn = null)
    {
        var messages = new List<ChatMessage>();
        var notes = new Dictionary<string, string>();

        foreach (var line in File.ReadLines(sessionPath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("data", out var dataElement))
                {
                    continue;
                }

                if (!dataElement.TryGetProperty("type", out var typeElement))
                {
                    continue;
                }

                var type = typeElement.GetString();

                // Parse work notes
                if (string.Equals(type, "work_note", StringComparison.OrdinalIgnoreCase))
                {
                    if (dataElement.TryGetProperty("key", out var keyEl)
                        && dataElement.TryGetProperty("value", out var valEl))
                    {
                        var key = keyEl.GetString();
                        var value = valEl.GetString();
                        if (!string.IsNullOrEmpty(key))
                        {
                            if (value == null)
                                notes.Remove(key);
                            else
                                notes[key] = value;
                        }
                    }
                    continue;
                }

                if (!string.Equals(type, "message", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!dataElement.TryGetProperty("role", out var roleElement))
                {
                    continue;
                }

                var role = roleElement.GetString();
                switch (role)
                {
                    case "user":
                        if (dataElement.TryGetProperty("content", out var userContent))
                        {
                            messages.Add(new UserMessage(userContent.GetString() ?? string.Empty));
                        }
                        break;
                    case "assistant":
                        messages.Add(ParseAssistantMessage(dataElement));
                        break;
                    case "tool":
                        var toolMessage = ParseToolMessage(dataElement);
                        if (toolMessage != null)
                        {
                            messages.Add(toolMessage);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                warn?.Invoke($"Failed to parse session log line: {ex.Message}");
            }
        }

        return (messages, notes);
    }

    private static AssistantMessage ParseAssistantMessage(JsonElement dataElement)
    {
        string? content = null;
        if (dataElement.TryGetProperty("content", out var contentElement))
        {
            content = contentElement.GetString();
        }

        IReadOnlyList<ToolCall>? toolCalls = null;
        if (dataElement.TryGetProperty("toolCalls", out var toolCallsElement)
            && toolCallsElement.ValueKind == JsonValueKind.Array)
        {
            var calls = new List<ToolCall>();
            foreach (var callElement in toolCallsElement.EnumerateArray())
            {
                var callId = callElement.GetProperty("callId").GetString();
                var name = callElement.GetProperty("name").GetString();
                var argsJson = callElement.GetProperty("argsJson").GetString();

                if (!string.IsNullOrEmpty(callId) && !string.IsNullOrEmpty(name) && argsJson != null)
                {
                    calls.Add(new ToolCall(callId, name, argsJson));
                }
            }

            if (calls.Count > 0)
            {
                toolCalls = calls;
            }
        }

        return new AssistantMessage
        {
            Content = content,
            ToolCalls = toolCalls
        };
    }

    private static ToolResultMessage? ParseToolMessage(JsonElement dataElement)
    {
        if (!dataElement.TryGetProperty("callId", out var callIdElement)
            || !dataElement.TryGetProperty("toolName", out var toolNameElement)
            || !dataElement.TryGetProperty("result", out var resultElement))
        {
            return null;
        }

        var callId = callIdElement.GetString();
        var toolName = toolNameElement.GetString();
        if (string.IsNullOrEmpty(callId) || string.IsNullOrEmpty(toolName))
        {
            return null;
        }

        var result = JsonSerializer.Deserialize<ToolResult>(resultElement.GetRawText(), SerializerOptions);
        if (result == null)
        {
            return null;
        }

        return new ToolResultMessage(callId, toolName, result);
    }

    private static List<ChatMessage> TakeLastTurns(List<ChatMessage> messages, int turnCount)
    {
        if (turnCount <= 0 || messages.Count == 0)
            return new List<ChatMessage>();

        // A "turn" starts with a UserMessage. Walk backwards to find the start of the Nth-last turn.
        var turnStarts = new List<int>();
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i] is UserMessage)
                turnStarts.Add(i);
        }

        if (turnStarts.Count == 0)
            return new List<ChatMessage>();

        var startIndex = turnStarts[Math.Max(0, turnStarts.Count - turnCount)];
        return messages.GetRange(startIndex, messages.Count - startIndex);
    }

    private static List<ChatMessage> GenerateSummaryMessages(List<ChatMessage> messages)
    {
        if (messages.Count == 0)
            return new List<ChatMessage>();

        // Extract metadata from the conversation
        var userPrompts = new List<string>();
        var toolNames = new HashSet<string>();
        int assistantCount = 0;

        foreach (var msg in messages)
        {
            switch (msg)
            {
                case UserMessage user:
                    userPrompts.Add(user.Content.Length > 80
                        ? user.Content[..80] + "..."
                        : user.Content);
                    break;
                case AssistantMessage:
                    assistantCount++;
                    break;
                case ToolResultMessage tool:
                    toolNames.Add(tool.ToolName);
                    break;
            }
        }

        var parts = new List<string>
        {
            $"Previous session: {userPrompts.Count} user messages, {assistantCount} assistant responses."
        };

        if (toolNames.Count > 0)
            parts.Add($"Tools used: {string.Join(", ", toolNames)}.");

        if (userPrompts.Count > 0)
            parts.Add($"Last user prompt: \"{userPrompts[^1]}\"");

        var summary = string.Join(" ", parts)
            + "\n\nPlease continue from where we left off. Ask if you need context about prior work.";

        return new List<ChatMessage> { new UserMessage(summary) };
    }
}
