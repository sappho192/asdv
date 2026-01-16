# Agent Architecture Guide

This document describes the agent architecture and how to extend the system.

## Overview

The Local Coding Agent follows an event-driven, provider-agnostic architecture. The core loop is:

1. **Receive** user prompt
2. **Stream** model response via `IModelProvider`
3. **Parse** events into standard `AgentEvent` types
4. **Execute** tool calls with policy checks
5. **Repeat** until completion or max iterations

## Core Components

### AgentOrchestrator

The orchestrator (`Agent.Core/Orchestrator/AgentOrchestrator.cs`) is the central coordinator:

```csharp
public class AgentOrchestrator
{
    public async Task RunAsync(string userPrompt, CancellationToken ct = default)
    {
        // 1. Initialize conversation with user message
        // 2. Loop: Stream from model -> Process events -> Execute tools
        // 3. Exit on completion or max iterations
    }
}
```

### Event Model

All providers emit a standard event stream (`Agent.Core/Events/AgentEvent.cs`):

| Event | Description |
|-------|-------------|
| `TextDelta` | Streamed text from the model |
| `ToolCallStarted` | Tool invocation begins |
| `ToolCallArgsDelta` | Streamed tool arguments (JSON fragments) |
| `ToolCallReady` | Tool call complete, ready for execution |
| `ToolResultEvent` | Tool execution result |
| `ResponseCompleted` | Model response finished |
| `TraceEvent` | Debug/unknown events |

### Message Types

The conversation history uses typed messages (`Agent.Core/Messages/ChatMessage.cs`):

- `UserMessage` - User input
- `AssistantMessage` - Model response (text and/or tool calls)
- `ToolResultMessage` - Tool execution results

## Adding a New Provider

1. Create a new project: `Agent.Llm.YourProvider`

2. Implement `IModelProvider`:

```csharp
public class YourProvider : IModelProvider
{
    public string Name => "yourprovider";

    public async IAsyncEnumerable<AgentEvent> StreamAsync(
        ModelRequest request,
        CancellationToken ct = default)
    {
        // 1. Build HTTP request for your API
        // 2. Stream SSE response
        // 3. Parse and yield AgentEvent instances
    }
}
```

3. Handle these responsibilities:
   - Convert `ModelRequest` to your API format
   - Parse streaming response (SSE)
   - Accumulate tool call arguments until complete
   - Emit `ToolCallReady` only when arguments are fully received
   - Handle unknown events gracefully with `TraceEvent`

4. Register in `Agent.Cli/Program.cs`:

```csharp
static IModelProvider CreateProvider(string provider, string model)
{
    return provider switch
    {
        "yourprovider" => new YourProvider(...),
        // ...
    };
}
```

## Adding a New Tool

1. Create a class in `Agent.Tools`:

```csharp
public class MyTool : ITool
{
    public string Name => "MyTool";
    public string Description => "Does something useful";
    public ToolPolicy Policy => new() { IsReadOnly = true };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "param1": { "type": "string", "description": "..." }
        },
        "required": ["param1"]
    }
    """;

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args,
        ToolContext ctx,
        CancellationToken ct)
    {
        var param1 = args.GetProperty("param1").GetString()!;

        // Do work...

        return ToolResult.Success(new { result = "..." });
    }
}
```

2. Register in `Agent.Cli/Program.cs`:

```csharp
static ToolRegistry CreateToolRegistry()
{
    var registry = new ToolRegistry();
    registry.Register(new MyTool());
    // ...
    return registry;
}
```

### Tool Guidelines

- **Use `ToolContext.Workspace`** for file path resolution
- **Return structured data** in `ToolResult.Data` for model consumption
- **Set appropriate `ToolPolicy`**:
  - `IsReadOnly = true` for read-only operations
  - `RequiresApproval = true` for dangerous operations
  - `Risk = RiskLevel.High` for potentially destructive operations
- **Handle errors gracefully** with `ToolResult.Failure()`

## Policy System

The policy engine (`Agent.Core/Policy/`) controls tool execution:

```csharp
public interface IPolicyEngine
{
    Task<PolicyDecision> EvaluateAsync(ITool tool, string argsJson);
}

public enum PolicyDecision
{
    Allowed,        // Execute immediately
    RequiresApproval, // Ask user first
    Denied          // Block execution
}
```

### Default Policy Rules

1. Tools with `RequiresApproval = true` always require approval
2. `RunCommand` with dangerous commands requires approval
3. Auto-approve mode (`--yes`) bypasses all approval checks

### Custom Policy Rules

Extend `DefaultPolicyEngine` or implement `IPolicyEngine`:

```csharp
public class CustomPolicyEngine : IPolicyEngine
{
    public Task<PolicyDecision> EvaluateAsync(ITool tool, string argsJson)
    {
        // Custom logic based on tool, args, or context
    }
}
```

## Workspace Safety

The workspace (`Agent.Workspace/LocalWorkspace.cs`) ensures file operations stay within bounds:

```csharp
public interface IWorkspace
{
    string Root { get; }
    string? ResolvePath(string relativePath);  // Returns null if unsafe
    bool IsPathSafe(string fullPath);
}
```

### Safety Checks

1. **Path traversal**: Blocks `../` escapes
2. **Absolute paths**: Rejects paths starting with `/` or `C:\`
3. **Symlink escape**: Validates symlink targets stay within root
4. **Prefix matching**: Ensures resolved path is truly under root

## Session Logging

The logger (`Agent.Logging/JsonlSessionLogger.cs`) records all activity:

```csharp
public interface ISessionLogger : IAsyncDisposable
{
    Task LogAsync<T>(T entry);
}
```

### Log Entry Format

```json
{"timestamp":"2024-01-15T10:30:00Z","data":{"type":"user_prompt","content":"..."}}
{"timestamp":"2024-01-15T10:30:01Z","data":{"type":"event","eventType":"TextDelta",...}}
{"timestamp":"2024-01-15T10:30:02Z","data":{"type":"tool_result","callId":"...",...}}
```

## Error Handling

### Provider Errors

- HTTP errors yield `TraceEvent("error", ...)` followed by `ResponseCompleted("error", null)`
- JSON parse errors yield `TraceEvent("parse_error", rawJson)` and continue

### Tool Errors

- Return `ToolResult.Failure(message)` with descriptive error
- Include `Diagnostics` for structured error information
- Never throw exceptions from tool execution

### Orchestrator Errors

- Catches exceptions and logs them
- Unknown tools return failure result
- Policy denials return failure result

## Testing

### Unit Tests

```csharp
[Fact]
public async Task MyTool_ValidInput_ReturnsSuccess()
{
    var tool = new MyTool();
    var args = JsonDocument.Parse("""{"param1": "value"}""").RootElement;
    var context = new ToolContext { ... };

    var result = await tool.ExecuteAsync(args, context, CancellationToken.None);

    result.Ok.Should().BeTrue();
}
```

### Integration Tests

For provider tests, mock the HTTP client or use a test server:

```csharp
[Fact]
public async Task Provider_StreamsEvents()
{
    var handler = new MockHttpHandler(sseResponse);
    var provider = new ClaudeProvider(new HttpClient(handler), "test-key");

    var events = await provider.StreamAsync(request).ToListAsync();

    events.Should().ContainItemsAssignableTo<TextDelta>();
}
```

## Performance Considerations

1. **Streaming**: Always use async enumerable for model responses
2. **Cancellation**: Propagate `CancellationToken` through all async calls
3. **Memory**: Don't buffer entire responses; process events as they arrive
4. **Timeouts**: Set appropriate timeouts for HTTP and subprocess calls
5. **Output limits**: Truncate large tool outputs to prevent context explosion
