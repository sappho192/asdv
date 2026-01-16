using Agent.Core.Tools;

namespace Agent.Core.Events;

public abstract record AgentEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record TextDelta(string Text) : AgentEvent;

public sealed record ToolCallStarted(string CallId, string ToolName) : AgentEvent;

public sealed record ToolCallArgsDelta(string CallId, string JsonFragment) : AgentEvent;

public sealed record ToolCallReady(string CallId, string ToolName, string ArgsJson) : AgentEvent;

public sealed record ToolResultEvent(string CallId, string ToolName, ToolResult Result) : AgentEvent;

public sealed record ResponseCompleted(
    string StopReason,
    UsageInfo? Usage,
    IDictionary<string, object>? ProviderMetadata = null
) : AgentEvent;

public sealed record TraceEvent(string Kind, string Data) : AgentEvent;

public sealed record UsageInfo(int InputTokens, int OutputTokens);
