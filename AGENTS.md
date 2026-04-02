# AGENTS.md — Working Guide for Coding Agents

Practical instructions for AI agents working on this repository.
Read this before making any changes.

## Project overview

ASDV is a .NET 8 coding agent that runs on local repositories.
The core loop lives in `Agent.Core`; CLI and HTTP server are thin consumers.
All agent output flows as `IAsyncEnumerable<AgentEvent>` — never break this contract.

```
src/
  Agent.Cli/           # Entry point, REPL, command parsing, console rendering
  Agent.Core/          # Orchestrator, events, policies, session, modes, workflows
    Orchestrator/      # AgentOrchestrator — the main loop
    Events/            # AgentEvent hierarchy
    Session/           # SessionState, TokenEstimator, ContextCompactor
    Modes/             # IExecutionMode, plan/review/implement/verify
    Workflows/         # WorkflowManifest, WorkflowLoader, WorkflowRunner
    Tools/             # ITool, ToolPolicy, ToolContext, ToolRegistry
    Policy/            # IPolicyEngine, DefaultPolicyEngine
    Providers/         # IModelProvider, IProviderCapabilities
  Agent.Tools/         # Tool implementations (ReadFile, FileEdit, RunCommand, …)
  Agent.Workspace/     # LocalWorkspace, WorktreeWorkspace — path safety
  Agent.Logging/       # JSONL session logger
  Agent.Llm.Anthropic/ # Claude provider
  Agent.Llm.OpenAI/    # OpenAI / OpenAI-compatible provider
  Agent.Server/        # ASP.NET Core HTTP + SSE server
tests/
  Agent.Core.Tests/
  Agent.Tools.Tests/
  Agent.Server.Tests/
```

## Build and test commands

```bash
# Build everything
dotnet build

# Run all tests (116 tests across 3 projects)
dotnet test

# Run a single test project
dotnet test tests/Agent.Core.Tests
dotnet test tests/Agent.Tools.Tests
dotnet test tests/Agent.Server.Tests

# Run a specific test by name pattern
dotnet test --filter "FullyQualifiedName~ContextCompactor"

# Run the CLI
dotnet run --project src/Agent.Cli

# Run the server
dotnet run --project src/Agent.Server
```

All tests must pass before committing. If you add a feature, add tests for it.

## Adding a new tool

1. Create a class in `Agent.Tools/`:

```csharp
public class MyTool : ITool
{
    public string Name => "MyTool";
    public string Description => "Does X";
    public ToolPolicy Policy => new() { IsReadOnly = true, IsConcurrencySafe = true };
    public string InputSchema => """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}""";

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var path = ctx.Workspace.ResolvePath(args.GetProperty("path").GetString()!);
        if (path is null) return ToolResult.Failure("Path outside workspace");
        // ...
        return ToolResult.Success(new { result = "..." });
    }
}
```

2. Register in both `Agent.Cli/Program.cs` and `Agent.Server/Program.cs` (both have a `CreateToolRegistry` function).

### ToolPolicy fields

| Field | Effect |
|-------|--------|
| `IsReadOnly` | Does not modify files or state |
| `IsConcurrencySafe` | Eligible for `Task.WhenAll` batching with other read-only tools |
| `RequiresApproval` | User must approve before execution |
| `Risk` | `Low` / `Medium` / `High` — High blocks auto-approve |
| `ProducesProgress` | Tool calls `ctx.Progress?.Report(...)` |
| `IsExternalSideEffect` | Affects systems outside the repo |

Consecutive tools with `IsReadOnly && IsConcurrencySafe` run in parallel via `Task.WhenAll`.
Non-parallel tools act as barriers — order is always preserved.

Always resolve file paths through `ctx.Workspace.ResolvePath()`. Never access the filesystem directly.

## Adding a new execution mode

Implement `IExecutionMode` in `Agent.Core/Modes/` and register in `ExecutionModeRegistry`:

```csharp
public class MyMode : IExecutionMode
{
    public string Name => "mymode";
    public string PromptFragment => "Focus only on ...";
    public Func<ITool, bool> ToolFilter => tool => tool.Policy.IsReadOnly;
}
```

`ToolFilter` restricts which tools are available in that mode.
`PromptFragment` is appended to the system prompt each turn.

## Adding a new provider

Implement `IModelProvider` (and optionally `IProviderCapabilities`) in a new `Agent.Llm.*` project:

```csharp
public class MyProvider : IModelProvider, IProviderCapabilities
{
    public string Name => "myprovider";
    public int ContextWindowTokens => 128_000;

    public async IAsyncEnumerable<AgentEvent> StreamAsync(ModelRequest request, CancellationToken ct)
    {
        // Yield TextDelta, ToolCallStarted, ToolCallArgsDelta, ToolCallReady, ResponseCompleted
    }
}
```

Register in the `CreateProvider` switch in `Agent.Cli/Program.cs` and `Agent.Server/Program.cs`.

## Session state

`SessionState` is mutable shared state passed through `AgentOptions.State`.
Stale reads are acceptable — the orchestrator writes it; CLI/server read it for display.
Notes in `SessionState.Notes` are persisted via session log tombstoning and survive resume.

`ContextCompactor.NeedsCompaction(messages, state.MaxContextTokens)` returns true at 80% of the context window.
When true, the orchestrator calls `CompactSlidingWindow` before the next request.
Compaction groups messages into turns (user → assistant → tool_results) and trims oldest turns first.
The first user message (the task) is always preserved.

## Session log format

Logs are JSONL files in `.agent/session_<id>.jsonl`.
`SessionLogReader` replays them to reconstruct conversation history on resume.
Work notes are stored as `work_note` entries (key/value) and `work_note_clear_all`.
The reader replays these in order to recover the final notes state.

## Testing conventions

- Tests use xUnit + FluentAssertions.
- Use `.Should().Be()`, `.Should().BeTrue()`, `.Should().Contain()` etc.
- Do NOT use `is` pattern matching inside FluentAssertions expression trees — use `.OfType<T>().Should()...` instead.
- Integration tests in `Agent.Tools.Tests` use a real temp directory; clean up with `TempDir` fixtures.
- For orchestrator/provider tests, use a mock `IModelProvider` that yields a fixed event sequence.

```csharp
// Correct
result.OfType<UserMessage>().Should().Contain(m => m.Content.Contains("compacted"));

// Wrong — throws at runtime
result.Should().Contain(m => m is UserMessage um && um.Content.Contains("compacted"));
```

## TikToken dependency

`Agent.Core` uses `Tiktoken` NuGet for token estimation.
Use `ModelToEncoder.For("gpt-4")` to get the cl100k_base encoder — not `ModelToEncoder.For("cl100k_base")` (that throws).
`Agent.Core.csproj` references `System.Text.Json` 9.0.5 to satisfy Tiktoken's transitive dependency.
Do not downgrade it.

## Workflow manifests

YAML files loaded by `WorkflowLoader`. Steps run sequentially; a failed step (AgentError or MaxIterationsReached) stops the workflow.

```yaml
name: "my-workflow"
steps:
  - mode: "plan"
    prompt: "Analyze and plan"
    maxIterations: 5
  - mode: "implement"
    prompt: "Implement the plan"
    maxIterations: 15
  - mode: "verify"
    maxIterations: 5
```

`mode` must match a name registered in `ExecutionModeRegistry`. An unknown mode yields `AgentError` and stops.

## Worktree isolation

`WorktreeWorkspace` creates a git worktree branch, runs the agent in it, then merges back.
Check exit codes of both `git add` and `git commit` before attempting merge — do not assume they succeed.

## PR conventions

- One logical change per commit.
- Commit message: short imperative summary, e.g. `Add WorktreeWorkspace for git isolation`.
- All 3 test projects must be green before opening a PR.
- The server (`Agent.Server`) and CLI (`Agent.Cli`) share the same `Agent.Core` orchestrator — changes to the orchestrator affect both surfaces.
