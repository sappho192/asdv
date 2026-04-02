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

// Orchestrator-level events (yielded by RunStreamAsync)

public sealed record IterationStarted(int Iteration, int MaxIterations) : AgentEvent;

public sealed record ToolExecutionStarted(string CallId, string ToolName, string ArgsJson) : AgentEvent;

public sealed record ToolExecutionCompleted(string CallId, string ToolName, ToolResult Result) : AgentEvent;

public sealed record ApprovalRequested(string CallId, string ToolName, string ArgsJson) : AgentEvent;

public sealed record ApprovalResult(string CallId, bool Approved) : AgentEvent;

public sealed record AssistantMessageCompleted(string? Text, int ToolCallCount) : AgentEvent;

public sealed record AgentCompleted(string Reason) : AgentEvent;

public sealed record AgentError(string Message) : AgentEvent;

public sealed record MaxIterationsReached(int Iterations) : AgentEvent;

// Session lifecycle events

public sealed record SessionStarted(string SessionId, string Provider, string Model, bool Resumed) : AgentEvent;

public sealed record SessionCompleted(string SessionId, string Reason, int TotalIterations) : AgentEvent;

public sealed record SessionError(string SessionId, string Message) : AgentEvent;
