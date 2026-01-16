# CLAUDE.md

This file provides guidance for AI assistants (like Claude) working on this codebase.

## Project Overview

This is a .NET 8 console-based coding agent that operates on local repositories. It supports both OpenAI and Anthropic as LLM providers with a provider-agnostic architecture.

## Build & Test Commands

```bash
# Build the solution
dotnet build

# Run all tests
dotnet test

# Run the agent
dotnet run --project src/Agent.Cli -- "your prompt here"

# Run with specific provider
dotnet run --project src/Agent.Cli -- -p openai "your prompt"
dotnet run --project src/Agent.Cli -- -p anthropic "your prompt"

# Run with debug output (shows stack traces and detailed errors)
dotnet run --project src/Agent.Cli -- -d -p openai "your prompt"
```

## Project Structure

```
src/
  Agent.Cli/           # Entry point, CLI argument parsing
  Agent.Core/          # Core interfaces, events, orchestrator
    Events/            # AgentEvent types (TextDelta, ToolCallReady, etc.)
    Messages/          # ChatMessage types (UserMessage, AssistantMessage, etc.)
    Tools/             # ITool interface, ToolResult, ToolRegistry
    Orchestrator/      # AgentOrchestrator, AgentOptions
    Policy/            # IPolicyEngine, PolicyDecision
    Approval/          # IApprovalService, ConsoleApprovalService
    Workspace/         # IWorkspace interface
    Logging/           # ISessionLogger interface
  Agent.Workspace/     # LocalWorkspace with path safety
  Agent.Tools/         # Tool implementations
  Agent.Llm.Anthropic/ # Claude provider with SSE streaming
  Agent.Llm.OpenAI/    # OpenAI provider with SSE streaming
  Agent.Logging/       # JsonlSessionLogger implementation
tests/
  Agent.Core.Tests/    # Core component tests
  Agent.Tools.Tests/   # Tool implementation tests
docs/
  DESIGN.md            # Design principles and architecture
  IMPLEMENTATION.md    # Detailed implementation guide
```

## Key Architectural Decisions

### Provider-Agnostic Design
- The orchestrator only works with `AgentEvent` streams
- Provider-specific details are encapsulated in `IModelProvider` implementations
- Adding a new provider requires implementing `IModelProvider.StreamAsync()`

### Event-Driven Model
- All model responses are streamed as `AgentEvent` instances
- Tool calls are accumulated until `ToolCallReady` signals completion
- The orchestrator never directly parses provider-specific formats

### Safety First
- All file paths go through `IWorkspace.ResolvePath()` for validation
- Tool execution is gated by `IPolicyEngine` decisions
- Dangerous operations require explicit user approval (unless `--yes`)

## Code Conventions

### File Organization
- One class per file (with exceptions for small related types)
- Interfaces in their own files prefixed with `I`
- Records used for immutable data types

### Naming
- Async methods end with `Async`
- Interfaces start with `I`
- Private fields prefixed with `_`

### Error Handling
- Tools return `ToolResult.Failure()` instead of throwing
- Providers yield `TraceEvent` for unknown/error events
- Never crash on unexpected input; log and continue

## Common Tasks

### Adding a New Tool

1. Create `src/Agent.Tools/MyTool.cs`:
```csharp
public class MyTool : ITool
{
    public string Name => "MyTool";
    public string Description => "What it does";
    public ToolPolicy Policy => new() { IsReadOnly = true };
    public string InputSchema => """{ ... }""";

    public async Task<ToolResult> ExecuteAsync(
        JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        // Implementation
        return ToolResult.Success(data);
    }
}
```

2. Register in `src/Agent.Cli/Program.cs`:
```csharp
registry.Register(new MyTool());
```

### Adding a New Provider

1. Create project: `dotnet new classlib -n Agent.Llm.NewProvider -o src/Agent.Llm.NewProvider`
2. Add reference to `Agent.Core`
3. Implement `IModelProvider` with SSE streaming
4. Register in `Agent.Cli/Program.cs`

### Modifying the System Prompt

Edit `GetSystemPrompt()` in `src/Agent.Cli/Program.cs`

## Testing Guidelines

- Use xUnit with FluentAssertions
- Mock external dependencies with Moq
- Create temp directories for file system tests (clean up in `Dispose`)
- Pass `CancellationToken.None` in tests

## Important Interfaces

### IModelProvider
```csharp
IAsyncEnumerable<AgentEvent> StreamAsync(ModelRequest request, CancellationToken ct)
```

### ITool
```csharp
Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext context, CancellationToken ct)
```

### IWorkspace
```csharp
string? ResolvePath(string relativePath)  // Returns null if unsafe
bool IsPathSafe(string fullPath)
```

### IPolicyEngine
```csharp
Task<PolicyDecision> EvaluateAsync(ITool tool, string argsJson)
```

## Debugging Tips

1. **Session logs**: Check `.agent/session_*.jsonl` for full event history
2. **Provider issues**: Look for `TraceEvent` in the event stream
3. **Tool failures**: Check `ToolResult.Diagnostics` for error details
4. **Path issues**: Verify `IWorkspace.ResolvePath()` returns non-null

## Dependencies

- `System.CommandLine` - CLI parsing
- `System.Text.Json` - JSON serialization
- `DotNetEnv` - .env file loading
- `Microsoft.Extensions.FileSystemGlobbing` - Glob pattern matching
- `FluentAssertions` - Test assertions
- `Moq` - Mocking framework

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `ANTHROPIC_API_KEY` | Anthropic API authentication |
| `OPENAI_API_KEY` | OpenAI API authentication |
| `OPENAI_BASE_URL` | Custom OpenAI-compatible endpoint |

**Note:** Environment variables can be loaded from a `.env` file in the repository root. The `.env` file is automatically loaded at startup.

## CLI Options

| Option | Alias | Description | Default |
|--------|-------|-------------|---------|
| `--repo` | `-r` | Repository root path | Current directory |
| `--provider` | `-p` | LLM provider (openai\|anthropic) | openai |
| `--model` | `-m` | Model name | Provider-specific |
| `--yes` | `-y` | Auto-approve all tool calls | false |
| `--session` | `-s` | Session log file path | Auto-generated |
| `--max-iterations` | | Maximum agent iterations | 20 |
| `--debug` | `-d` | Enable debug output (stack traces) | false |
