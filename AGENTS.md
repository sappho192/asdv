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
    public async Task RunAsync(
        string userPrompt,
        List<ChatMessage>? messages = null,
        CancellationToken ct = default)
    {
        // 1. Initialize or resume conversation
        // 2. Add user message to conversation
        // 3. Loop: Stream from model -> Process events -> Execute tools
        // 4. Log all messages to session logger
        // 5. Exit on completion or max iterations
    }
}
```

**Session Resumption**: The orchestrator can now accept a list of previous messages to resume conversations. This enables REPL mode and session persistence.

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
{"timestamp":"2024-01-15T10:30:00Z","data":{"type":"session_start","sessionId":"abc123","mode":"repl","resumed":false}}
{"timestamp":"2024-01-15T10:30:00Z","data":{"type":"user_prompt","content":"..."}}
{"timestamp":"2024-01-15T10:30:00Z","data":{"type":"message","role":"user","content":"..."}}
{"timestamp":"2024-01-15T10:30:01Z","data":{"type":"event","eventType":"TextDelta",...}}
{"timestamp":"2024-01-15T10:30:02Z","data":{"type":"message","role":"assistant","content":"...","toolCalls":[...]}}
{"timestamp":"2024-01-15T10:30:03Z","data":{"type":"message","role":"tool","callId":"...","result":{...}}}
```

### Session Resumption

The `SessionLogReader` class (`Agent.Cli/SessionLogReader.cs`) can parse session logs and reconstruct the conversation history:

```csharp
var messages = SessionLogReader.LoadMessages(sessionPath, warning =>
{
    Console.WriteLine($"Warning: {warning}");
});
```

This enables:
- Resuming conversations across CLI invocations
- REPL mode with persistent context
- Debugging and replay of agent sessions

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

## Agent.Server Architecture

`Agent.Server` is an ASP.NET Core web application that exposes agent functionality over HTTP with Server-Sent Events (SSE) for streaming.

### Key Components

#### Session Management (`InMemorySessionStore`)

Manages concurrent agent sessions with thread-safe access:

```csharp
public interface ISessionStore
{
    SessionRuntime Create(CreateSessionRequest request);
    bool TryAdd(SessionRuntime session);
    bool TryGet(string id, out SessionRuntime session);
}
```

#### Session Runtime (`SessionRuntime`)

Encapsulates a running agent session:

- **AgentOrchestrator**: Core agent logic
- **ServerApprovalService**: Handles async approval requests
- **Event channel**: Broadcasts server events to SSE clients
- **Stream lock**: Ensures only one SSE connection per session

#### Server Events (`ServerEvent`)

Standardized event format for SSE streaming:

| Event Type | Description |
|------------|-------------|
| `text_delta` | Streamed text from model |
| `tool_call` | Tool invocation started |
| `tool_result` | Tool execution completed |
| `approval_required` | Waiting for user approval |
| `completed` | Agent iteration finished |
| `error` | Error occurred |
| `trace` | Debug/diagnostic information |

#### Approval Service (`ServerApprovalService`)

Non-blocking approval workflow using `TaskCompletionSource`:

```csharp
public class ServerApprovalService : IApprovalService
{
    public async Task<bool> RequestApprovalAsync(string callId, ITool tool, string argsJson)
    {
        // 1. Emit approval_required event
        // 2. Create TaskCompletionSource for this callId
        // 3. Wait for client to POST to /api/sessions/{id}/approvals/{callId}
        // 4. Return approval decision
    }
}
```

### API Workflow

1. **Create Session**: `POST /api/sessions` → Creates runtime, returns session ID
2. **Connect SSE**: `GET /api/sessions/{id}/stream` → Opens event stream
3. **Send Message**: `POST /api/sessions/{id}/chat` → Triggers agent loop
4. **Stream Events**: Server pushes events via SSE as agent processes
5. **Handle Approvals**: Client posts approval decision when prompted
6. **Repeat**: Client sends more messages as needed

### Session Resumption

The server supports resuming sessions from JSONL logs:

```csharp
POST /api/sessions/{id}/resume
{
  "workspacePath": "/path/to/repo",
  "provider": "openai",
  "model": "gpt-5-mini"
}
```

The `SessionLogReader` parses the log file and reconstructs the conversation history, enabling stateless server restarts while preserving context.

### Client Implementation

The `server-client.mjs` script demonstrates:

- Creating/resuming sessions
- SSE stream parsing
- Interactive approval handling
- REPL-style user interaction

### Adding New Server Features

To add a new server capability:

1. **Define the event**: Add to `ServerEvent` in `Models/ServerEvents.cs`
2. **Emit from runtime**: Update `SessionRunner` to broadcast the event
3. **Handle in client**: Add case to `handleEvent()` in client script
4. **Test**: Add test case in `Agent.Server.Tests`

### Server Configuration

Environment variables:

- `ASPNETCORE_URLS`: Server bind address (default: `http://localhost:5000`)
- `ANTHROPIC_API_KEY` / `OPENAI_API_KEY`: LLM provider credentials
- `OPENAI_BASE_URL`: Custom OpenAI-compatible endpoint
