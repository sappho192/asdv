using System.Diagnostics.CodeAnalysis;
using Agent.Core.Tools;

namespace Agent.Core.Messages;

public abstract record ChatMessage
{
    public string Role { get; init; } = null!;
}

public sealed record UserMessage : ChatMessage
{
    public required string Content { get; init; }

    [SetsRequiredMembers]
    public UserMessage(string content)
    {
        Role = "user";
        Content = content;
    }

    public UserMessage() => Role = "user";
}

public sealed record AssistantMessage : ChatMessage
{
    public string? Content { get; init; }
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }

    public AssistantMessage() => Role = "assistant";
}

public sealed record ToolResultMessage : ChatMessage
{
    public required string CallId { get; init; }
    public required string ToolName { get; init; }
    public required ToolResult Result { get; init; }

    [SetsRequiredMembers]
    public ToolResultMessage(string callId, string toolName, ToolResult result)
    {
        Role = "tool";
        CallId = callId;
        ToolName = toolName;
        Result = result;
    }

    public ToolResultMessage() => Role = "tool";
}

public sealed record ToolCall(string CallId, string Name, string ArgsJson);
