using Agent.Core.Tools;

namespace Agent.Server.Models;

public abstract record ServerEvent(string Type);

public sealed record TextDeltaEvent(string Text) : ServerEvent("text_delta");

public sealed record ToolCallEvent(string CallId, string Tool, string ArgsJson)
    : ServerEvent("tool_call");

public sealed record ApprovalRequiredEvent(string CallId, string Tool, string ArgsJson, string Reason)
    : ServerEvent("approval_required");

public sealed record ToolResultEvent(string CallId, string Tool, ToolResult Result)
    : ServerEvent("tool_result");

public sealed record CompletedEvent(string? StopReason) : ServerEvent("completed");

public sealed record TraceEvent(string Kind, string Data) : ServerEvent("trace");

public sealed record ErrorEvent(string Message) : ServerEvent("error");
