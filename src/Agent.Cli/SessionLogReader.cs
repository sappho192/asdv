using System.Text.Json;
using Agent.Core.Messages;
using Agent.Core.Tools;

namespace Agent.Cli;

internal static class SessionLogReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static List<ChatMessage> LoadMessages(string sessionPath, Action<string>? warn = null)
    {
        var messages = new List<ChatMessage>();
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

        return messages;
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
}
