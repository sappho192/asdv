# Implementation Guide

This document provides detailed implementation guidance based on the design specifications in DESIGN.md.

---

## 1. Project Setup

### 1.1 Solution Structure

```bash
dotnet new sln -n Agent

# Create projects
dotnet new classlib -n Agent.Core -o src/Agent.Core
dotnet new classlib -n Agent.Tools -o src/Agent.Tools
dotnet new classlib -n Agent.Workspace -o src/Agent.Workspace
dotnet new classlib -n Agent.Llm.OpenAI -o src/Agent.Llm.OpenAI
dotnet new classlib -n Agent.Llm.Anthropic -o src/Agent.Llm.Anthropic
dotnet new classlib -n Agent.Logging -o src/Agent.Logging
dotnet new console -n Agent.Cli -o src/Agent.Cli

# Create test projects
dotnet new xunit -n Agent.Core.Tests -o tests/Agent.Core.Tests
dotnet new xunit -n Agent.Tools.Tests -o tests/Agent.Tools.Tests

# Add projects to solution
dotnet sln add src/Agent.Core/Agent.Core.csproj
dotnet sln add src/Agent.Tools/Agent.Tools.csproj
dotnet sln add src/Agent.Workspace/Agent.Workspace.csproj
dotnet sln add src/Agent.Llm.OpenAI/Agent.Llm.OpenAI.csproj
dotnet sln add src/Agent.Llm.Anthropic/Agent.Llm.Anthropic.csproj
dotnet sln add src/Agent.Logging/Agent.Logging.csproj
dotnet sln add src/Agent.Cli/Agent.Cli.csproj
dotnet sln add tests/Agent.Core.Tests/Agent.Core.Tests.csproj
dotnet sln add tests/Agent.Tools.Tests/Agent.Tools.Tests.csproj
```

### 1.2 Project Dependencies

```
Agent.Cli
  └── Agent.Core
  └── Agent.Tools
  └── Agent.Workspace
  └── Agent.Llm.OpenAI
  └── Agent.Llm.Anthropic
  └── Agent.Logging

Agent.Tools
  └── Agent.Core
  └── Agent.Workspace

Agent.Llm.OpenAI
  └── Agent.Core

Agent.Llm.Anthropic
  └── Agent.Core

Agent.Logging
  └── Agent.Core
```

### 1.3 Required NuGet Packages

```xml
<!-- Agent.Core -->
<PackageReference Include="System.Text.Json" Version="8.0.0" />

<!-- Agent.Llm.* -->
<PackageReference Include="System.Net.Http.Json" Version="8.0.0" />

<!-- Agent.Cli -->
<PackageReference Include="System.CommandLine" Version="2.0.0-beta4.22272.1" />
<PackageReference Include="Spectre.Console" Version="0.49.1" />

<!-- Agent.Tools (for patch application) -->
<PackageReference Include="DiffPlex" Version="1.7.2" />

<!-- Tests -->
<PackageReference Include="xunit" Version="2.6.6" />
<PackageReference Include="Moq" Version="4.20.70" />
<PackageReference Include="FluentAssertions" Version="6.12.0" />
```

---

## 2. Agent.Core Implementation

### 2.1 AgentEvent (Standard Event Model)

```csharp
// src/Agent.Core/Events/AgentEvent.cs
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
```

### 2.2 ToolResult

```csharp
// src/Agent.Core/Tools/ToolResult.cs
namespace Agent.Core.Tools;

public sealed record ToolResult
{
    public bool Ok { get; init; }
    public string? Stdout { get; init; }
    public string? Stderr { get; init; }
    public object? Data { get; init; }
    public IReadOnlyList<Diagnostic>? Diagnostics { get; init; }

    public static ToolResult Success(object? data = null, string? stdout = null) =>
        new() { Ok = true, Data = data, Stdout = stdout };

    public static ToolResult Failure(string message, string? stderr = null) =>
        new() { Ok = false, Stderr = stderr, Diagnostics = [new Diagnostic("Error", message)] };
}

public sealed record Diagnostic(string Code, string Message, object? Details = null);
```

### 2.3 ITool Interface

```csharp
// src/Agent.Core/Tools/ITool.cs
namespace Agent.Core.Tools;

public interface ITool
{
    string Name { get; }
    string Description { get; }
    string InputSchema { get; } // JSON Schema
    ToolPolicy Policy { get; }

    Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext context, CancellationToken ct = default);
}

public record ToolPolicy
{
    public bool RequiresApproval { get; init; }
    public bool IsReadOnly { get; init; }
    public RiskLevel Risk { get; init; } = RiskLevel.Low;
}

public enum RiskLevel { Low, Medium, High }

public record ToolContext
{
    public required string RepoRoot { get; init; }
    public required IWorkspace Workspace { get; init; }
    public required IApprovalService ApprovalService { get; init; }
}
```

### 2.4 IModelProvider Interface

```csharp
// src/Agent.Core/Providers/IModelProvider.cs
namespace Agent.Core.Providers;

public interface IModelProvider
{
    string Name { get; }

    IAsyncEnumerable<AgentEvent> StreamAsync(
        ModelRequest request,
        CancellationToken ct = default
    );
}

public record ModelRequest
{
    public required string Model { get; init; }
    public string? SystemPrompt { get; init; }
    public required IReadOnlyList<ChatMessage> Messages { get; init; }
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }
    public int? MaxTokens { get; init; }
    public double? Temperature { get; init; }
}

public record ToolDefinition(string Name, string Description, string InputSchema);
```

### 2.5 ChatMessage

```csharp
// src/Agent.Core/Messages/ChatMessage.cs
namespace Agent.Core.Messages;

public abstract record ChatMessage
{
    public required string Role { get; init; }
}

public sealed record UserMessage : ChatMessage
{
    public required string Content { get; init; }
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
    public ToolResultMessage() => Role = "tool";
}

public sealed record ToolCall(string CallId, string Name, string ArgsJson);
```

### 2.6 AgentOrchestrator

```csharp
// src/Agent.Core/Orchestrator/AgentOrchestrator.cs
namespace Agent.Core.Orchestrator;

public class AgentOrchestrator
{
    private readonly IModelProvider _provider;
    private readonly ToolRegistry _toolRegistry;
    private readonly IApprovalService _approvalService;
    private readonly IPolicyEngine _policyEngine;
    private readonly ISessionLogger _logger;
    private readonly AgentOptions _options;

    public AgentOrchestrator(
        IModelProvider provider,
        ToolRegistry toolRegistry,
        IApprovalService approvalService,
        IPolicyEngine policyEngine,
        ISessionLogger logger,
        AgentOptions options)
    {
        _provider = provider;
        _toolRegistry = toolRegistry;
        _approvalService = approvalService;
        _policyEngine = policyEngine;
        _logger = logger;
        _options = options;
    }

    public async Task RunAsync(string userPrompt, CancellationToken ct = default)
    {
        var messages = new List<ChatMessage>
        {
            new UserMessage { Content = userPrompt }
        };

        await _logger.LogAsync(new { type = "user_prompt", content = userPrompt });

        for (int iteration = 0; iteration < _options.MaxIterations; iteration++)
        {
            var request = BuildRequest(messages);
            var pendingToolCalls = new List<ToolCallReady>();
            var textBuffer = new StringBuilder();

            await foreach (var evt in _provider.StreamAsync(request, ct))
            {
                await _logger.LogAsync(evt);

                switch (evt)
                {
                    case TextDelta delta:
                        Console.Write(delta.Text);
                        textBuffer.Append(delta.Text);
                        break;

                    case ToolCallReady ready:
                        pendingToolCalls.Add(ready);
                        break;

                    case ResponseCompleted completed:
                        if (completed.StopReason == "end_turn" && pendingToolCalls.Count == 0)
                        {
                            Console.WriteLine("\n[Agent completed]");
                            return;
                        }
                        break;
                }
            }

            // Add assistant message
            if (textBuffer.Length > 0 || pendingToolCalls.Count > 0)
            {
                messages.Add(new AssistantMessage
                {
                    Content = textBuffer.Length > 0 ? textBuffer.ToString() : null,
                    ToolCalls = pendingToolCalls.Select(tc =>
                        new ToolCall(tc.CallId, tc.ToolName, tc.ArgsJson)).ToList()
                });
            }

            // Execute tool calls
            if (pendingToolCalls.Count > 0)
            {
                foreach (var toolCall in pendingToolCalls)
                {
                    var result = await ExecuteToolCallAsync(toolCall, ct);
                    messages.Add(new ToolResultMessage
                    {
                        CallId = toolCall.CallId,
                        ToolName = toolCall.ToolName,
                        Result = result
                    });
                }
            }
            else
            {
                break; // No tool calls, done
            }
        }

        Console.WriteLine("\n[Max iterations reached]");
    }

    private async Task<ToolResult> ExecuteToolCallAsync(ToolCallReady toolCall, CancellationToken ct)
    {
        Console.WriteLine($"\n[tool] {toolCall.ToolName}");

        var tool = _toolRegistry.GetTool(toolCall.ToolName);
        if (tool == null)
        {
            return ToolResult.Failure($"Unknown tool: {toolCall.ToolName}");
        }

        // Policy check
        var decision = await _policyEngine.EvaluateAsync(tool, toolCall.ArgsJson);
        if (decision == PolicyDecision.Denied)
        {
            return ToolResult.Failure("Tool execution denied by policy");
        }

        if (decision == PolicyDecision.RequiresApproval)
        {
            var approved = await _approvalService.RequestApprovalAsync(
                tool.Name, toolCall.ArgsJson, ct);
            if (!approved)
            {
                return ToolResult.Failure("User denied approval");
            }
        }

        try
        {
            var args = JsonDocument.Parse(toolCall.ArgsJson).RootElement;
            var context = new ToolContext
            {
                RepoRoot = _options.RepoRoot,
                Workspace = _options.Workspace,
                ApprovalService = _approvalService
            };

            var result = await tool.ExecuteAsync(args, context, ct);
            await _logger.LogAsync(new
            {
                type = "tool_result",
                callId = toolCall.CallId,
                tool = toolCall.ToolName,
                ok = result.Ok,
                diagnostics = result.Diagnostics
            });

            return result;
        }
        catch (Exception ex)
        {
            return ToolResult.Failure($"Tool execution failed: {ex.Message}");
        }
    }

    private ModelRequest BuildRequest(List<ChatMessage> messages)
    {
        return new ModelRequest
        {
            Model = _options.Model,
            SystemPrompt = _options.SystemPrompt,
            Messages = messages,
            Tools = _toolRegistry.GetToolDefinitions(),
            MaxTokens = _options.MaxTokens,
            Temperature = _options.Temperature
        };
    }
}
```

### 2.7 Policy Engine

```csharp
// src/Agent.Core/Policy/IPolicyEngine.cs
namespace Agent.Core.Policy;

public interface IPolicyEngine
{
    Task<PolicyDecision> EvaluateAsync(ITool tool, string argsJson);
}

public enum PolicyDecision
{
    Allowed,
    RequiresApproval,
    Denied
}

// src/Agent.Core/Policy/DefaultPolicyEngine.cs
public class DefaultPolicyEngine : IPolicyEngine
{
    private readonly PolicyOptions _options;

    public DefaultPolicyEngine(PolicyOptions options)
    {
        _options = options;
    }

    public Task<PolicyDecision> EvaluateAsync(ITool tool, string argsJson)
    {
        // Auto-approve if --yes flag is set
        if (_options.AutoApprove)
        {
            return Task.FromResult(PolicyDecision.Allowed);
        }

        // Check tool policy
        if (tool.Policy.RequiresApproval)
        {
            return Task.FromResult(PolicyDecision.RequiresApproval);
        }

        // Additional checks based on args
        if (tool.Name == "RunCommand")
        {
            var args = JsonDocument.Parse(argsJson).RootElement;
            if (IsDangerousCommand(args))
            {
                return Task.FromResult(PolicyDecision.RequiresApproval);
            }
        }

        return Task.FromResult(PolicyDecision.Allowed);
    }

    private bool IsDangerousCommand(JsonElement args)
    {
        if (!args.TryGetProperty("exe", out var exe)) return false;

        var command = exe.GetString()?.ToLowerInvariant() ?? "";
        var dangerous = new[] { "rm", "del", "rmdir", "format", "curl", "wget", "ssh", "powershell", "cmd" };

        return dangerous.Any(d => command.Contains(d));
    }
}
```

### 2.8 Approval Service

```csharp
// src/Agent.Core/Approval/IApprovalService.cs
namespace Agent.Core.Approval;

public interface IApprovalService
{
    Task<bool> RequestApprovalAsync(string toolName, string argsJson, CancellationToken ct = default);
}

// src/Agent.Core/Approval/ConsoleApprovalService.cs
public class ConsoleApprovalService : IApprovalService
{
    public Task<bool> RequestApprovalAsync(string toolName, string argsJson, CancellationToken ct = default)
    {
        Console.WriteLine();
        Console.WriteLine($"[Approval Required] Tool: {toolName}");
        Console.WriteLine($"Args: {argsJson}");
        Console.Write("Approve? (y/N): ");

        var input = Console.ReadLine();
        return Task.FromResult(
            input?.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) == true
        );
    }
}
```

---

## 3. Agent.Llm.Anthropic Implementation

### 3.1 Claude Provider

```csharp
// src/Agent.Llm.Anthropic/ClaudeProvider.cs
namespace Agent.Llm.Anthropic;

public class ClaudeProvider : IModelProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private const string BaseUrl = "https://api.anthropic.com/v1/messages";

    public string Name => "anthropic";

    public ClaudeProvider(HttpClient httpClient, string apiKey)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
    }

    public async IAsyncEnumerable<AgentEvent> StreamAsync(
        ModelRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var httpRequest = BuildRequest(request);

        using var response = await _httpClient.SendAsync(
            httpRequest,
            HttpCompletionOption.ResponseHeadersRead,
            ct);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        var toolCallBuffers = new Dictionary<int, ToolCallBuffer>();
        UsageInfo? usage = null;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var json = line[6..];
            if (json == "[DONE]") break;

            var evt = JsonDocument.Parse(json).RootElement;
            var eventType = evt.GetProperty("type").GetString();

            switch (eventType)
            {
                case "message_start":
                    if (evt.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("usage", out var u))
                    {
                        usage = new UsageInfo(
                            u.GetProperty("input_tokens").GetInt32(),
                            0);
                    }
                    break;

                case "content_block_start":
                    var index = evt.GetProperty("index").GetInt32();
                    var block = evt.GetProperty("content_block");
                    var blockType = block.GetProperty("type").GetString();

                    if (blockType == "tool_use")
                    {
                        var callId = block.GetProperty("id").GetString()!;
                        var toolName = block.GetProperty("name").GetString()!;
                        toolCallBuffers[index] = new ToolCallBuffer(callId, toolName);
                        yield return new ToolCallStarted(callId, toolName);
                    }
                    break;

                case "content_block_delta":
                    var deltaIndex = evt.GetProperty("index").GetInt32();
                    var delta = evt.GetProperty("delta");
                    var deltaType = delta.GetProperty("type").GetString();

                    if (deltaType == "text_delta")
                    {
                        var text = delta.GetProperty("text").GetString()!;
                        yield return new TextDelta(text);
                    }
                    else if (deltaType == "input_json_delta")
                    {
                        var partial = delta.GetProperty("partial_json").GetString()!;
                        if (toolCallBuffers.TryGetValue(deltaIndex, out var buffer))
                        {
                            buffer.AppendArgs(partial);
                            yield return new ToolCallArgsDelta(buffer.CallId, partial);
                        }
                    }
                    break;

                case "content_block_stop":
                    var stopIndex = evt.GetProperty("index").GetInt32();
                    if (toolCallBuffers.TryGetValue(stopIndex, out var completedBuffer))
                    {
                        yield return new ToolCallReady(
                            completedBuffer.CallId,
                            completedBuffer.ToolName,
                            completedBuffer.ArgsJson);
                    }
                    break;

                case "message_delta":
                    var stopReason = evt.GetProperty("delta")
                        .GetProperty("stop_reason").GetString() ?? "unknown";

                    int outputTokens = 0;
                    if (evt.TryGetProperty("usage", out var deltaUsage))
                    {
                        outputTokens = deltaUsage.GetProperty("output_tokens").GetInt32();
                    }

                    yield return new ResponseCompleted(
                        stopReason,
                        usage != null ? usage with { OutputTokens = outputTokens } : null);
                    break;

                default:
                    yield return new TraceEvent(eventType ?? "unknown", json);
                    break;
            }
        }
    }

    private HttpRequestMessage BuildRequest(ModelRequest request)
    {
        var body = new
        {
            model = request.Model,
            max_tokens = request.MaxTokens ?? 4096,
            system = request.SystemPrompt,
            messages = ConvertMessages(request.Messages),
            tools = request.Tools?.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                input_schema = JsonDocument.Parse(t.InputSchema).RootElement
            }),
            stream = true
        };

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = JsonContent.Create(body)
        };

        httpRequest.Headers.Add("x-api-key", _apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        return httpRequest;
    }

    private object[] ConvertMessages(IReadOnlyList<ChatMessage> messages)
    {
        var result = new List<object>();

        foreach (var msg in messages)
        {
            switch (msg)
            {
                case UserMessage user:
                    result.Add(new { role = "user", content = user.Content });
                    break;

                case AssistantMessage assistant:
                    var content = new List<object>();
                    if (!string.IsNullOrEmpty(assistant.Content))
                    {
                        content.Add(new { type = "text", text = assistant.Content });
                    }
                    if (assistant.ToolCalls != null)
                    {
                        foreach (var tc in assistant.ToolCalls)
                        {
                            content.Add(new
                            {
                                type = "tool_use",
                                id = tc.CallId,
                                name = tc.Name,
                                input = JsonDocument.Parse(tc.ArgsJson).RootElement
                            });
                        }
                    }
                    result.Add(new { role = "assistant", content });
                    break;

                case ToolResultMessage toolResult:
                    // Claude expects tool_result in user message content
                    var toolContent = new[]
                    {
                        new
                        {
                            type = "tool_result",
                            tool_use_id = toolResult.CallId,
                            content = toolResult.Result.Ok
                                ? JsonSerializer.Serialize(toolResult.Result.Data ?? toolResult.Result.Stdout)
                                : toolResult.Result.Stderr ?? "Error"
                        }
                    };
                    result.Add(new { role = "user", content = toolContent });
                    break;
            }
        }

        return result.ToArray();
    }

    private class ToolCallBuffer
    {
        public string CallId { get; }
        public string ToolName { get; }
        private readonly StringBuilder _args = new();

        public ToolCallBuffer(string callId, string toolName)
        {
            CallId = callId;
            ToolName = toolName;
        }

        public void AppendArgs(string fragment) => _args.Append(fragment);
        public string ArgsJson => _args.ToString();
    }
}
```

---

## 4. Agent.Tools Implementation

### 4.1 ReadFile Tool

```csharp
// src/Agent.Tools/ReadFileTool.cs
namespace Agent.Tools;

public class ReadFileTool : ITool
{
    public string Name => "ReadFile";
    public string Description => "Read contents of a file within the repository";
    public ToolPolicy Policy => new() { IsReadOnly = true };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "path": { "type": "string", "description": "Relative path to the file" },
            "startLine": { "type": "integer", "description": "Start line (1-indexed)" },
            "endLine": { "type": "integer", "description": "End line (1-indexed)" }
        },
        "required": ["path"]
    }
    """;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var path = args.GetProperty("path").GetString()!;
        var fullPath = ctx.Workspace.ResolvePath(path);

        if (fullPath == null)
        {
            return ToolResult.Failure($"Path traversal detected or path outside repo: {path}");
        }

        if (!File.Exists(fullPath))
        {
            return ToolResult.Failure($"File not found: {path}");
        }

        var lines = await File.ReadAllLinesAsync(fullPath, ct);

        int startLine = args.TryGetProperty("startLine", out var s) ? s.GetInt32() : 1;
        int endLine = args.TryGetProperty("endLine", out var e) ? e.GetInt32() : lines.Length;

        startLine = Math.Max(1, startLine);
        endLine = Math.Min(lines.Length, endLine);

        var selectedLines = lines.Skip(startLine - 1).Take(endLine - startLine + 1);
        var content = string.Join(Environment.NewLine, selectedLines);

        return ToolResult.Success(new
        {
            path,
            startLine,
            endLine,
            totalLines = lines.Length,
            content
        });
    }
}
```

### 4.2 SearchText Tool

```csharp
// src/Agent.Tools/SearchTextTool.cs
namespace Agent.Tools;

public class SearchTextTool : ITool
{
    public string Name => "SearchText";
    public string Description => "Search for text patterns in the repository";
    public ToolPolicy Policy => new() { IsReadOnly = true };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "query": { "type": "string", "description": "Search pattern (regex supported)" },
            "includeGlobs": { "type": "array", "items": { "type": "string" } },
            "excludeGlobs": { "type": "array", "items": { "type": "string" } },
            "maxResults": { "type": "integer", "default": 50 }
        },
        "required": ["query"]
    }
    """;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var query = args.GetProperty("query").GetString()!;
        var maxResults = args.TryGetProperty("maxResults", out var m) ? m.GetInt32() : 50;

        // Use ripgrep if available, otherwise fallback to manual search
        var rgPath = FindRipgrep();
        if (rgPath != null)
        {
            return await SearchWithRipgrepAsync(rgPath, query, ctx.RepoRoot, maxResults, ct);
        }

        return await SearchManuallyAsync(query, ctx.RepoRoot, maxResults, ct);
    }

    private string? FindRipgrep()
    {
        var paths = new[] { "rg", "rg.exe", "/usr/bin/rg", "/usr/local/bin/rg" };
        // Implementation: check if rg exists in PATH
        return null; // Simplified
    }

    private async Task<ToolResult> SearchWithRipgrepAsync(
        string rgPath, string query, string root, int maxResults, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = rgPath,
            Arguments = $"--json -m {maxResults} \"{query}\"",
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return ToolResult.Success(new { matches = output }, output);
    }

    private async Task<ToolResult> SearchManuallyAsync(
        string query, string root, int maxResults, CancellationToken ct)
    {
        var regex = new Regex(query, RegexOptions.IgnoreCase);
        var results = new List<object>();

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (results.Count >= maxResults) break;
            if (ShouldSkipFile(file)) continue;

            var lines = await File.ReadAllLinesAsync(file, ct);
            for (int i = 0; i < lines.Length && results.Count < maxResults; i++)
            {
                if (regex.IsMatch(lines[i]))
                {
                    results.Add(new
                    {
                        file = Path.GetRelativePath(root, file),
                        line = i + 1,
                        content = lines[i].Trim()
                    });
                }
            }
        }

        return ToolResult.Success(new { matches = results });
    }

    private bool ShouldSkipFile(string path)
    {
        var skipDirs = new[] { ".git", "node_modules", "bin", "obj", ".vs" };
        return skipDirs.Any(d => path.Contains(Path.DirectorySeparatorChar + d + Path.DirectorySeparatorChar));
    }
}
```

### 4.3 ApplyPatch Tool

```csharp
// src/Agent.Tools/ApplyPatchTool.cs
namespace Agent.Tools;

public class ApplyPatchTool : ITool
{
    public string Name => "ApplyPatch";
    public string Description => "Apply a unified diff patch to files in the repository";
    public ToolPolicy Policy => new() { RequiresApproval = true, Risk = RiskLevel.Medium };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "patch": { "type": "string", "description": "Unified diff format patch" }
        },
        "required": ["patch"]
    }
    """;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var patch = args.GetProperty("patch").GetString()!;

        // Try git apply first
        var gitResult = await TryGitApplyAsync(patch, ctx.RepoRoot, ct);
        if (gitResult.Ok)
        {
            return gitResult;
        }

        // Fallback to manual patch application
        return await ApplyPatchManuallyAsync(patch, ctx, ct);
    }

    private async Task<ToolResult> TryGitApplyAsync(string patch, string root, CancellationToken ct)
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, patch, ct);

            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"apply --check \"{tempFile}\"",
                WorkingDirectory = root,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            using var checkProcess = Process.Start(psi)!;
            var checkError = await checkProcess.StandardError.ReadToEndAsync(ct);
            await checkProcess.WaitForExitAsync(ct);

            if (checkProcess.ExitCode != 0)
            {
                return ToolResult.Failure("Patch check failed", checkError);
            }

            // Actually apply
            psi.Arguments = $"apply \"{tempFile}\"";
            using var applyProcess = Process.Start(psi)!;
            var applyError = await applyProcess.StandardError.ReadToEndAsync(ct);
            await applyProcess.WaitForExitAsync(ct);

            if (applyProcess.ExitCode != 0)
            {
                return ToolResult.Failure("Patch application failed", applyError);
            }

            return ToolResult.Success(new { method = "git apply", applied = true });
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    private async Task<ToolResult> ApplyPatchManuallyAsync(string patch, ToolContext ctx, CancellationToken ct)
    {
        // Parse unified diff and apply changes
        var hunks = ParseUnifiedDiff(patch);
        var appliedFiles = new List<string>();
        var failedHunks = new List<object>();

        foreach (var fileHunk in hunks)
        {
            var fullPath = ctx.Workspace.ResolvePath(fileHunk.FilePath);
            if (fullPath == null)
            {
                failedHunks.Add(new { file = fileHunk.FilePath, reason = "Path traversal detected" });
                continue;
            }

            try
            {
                var result = await ApplyHunksToFileAsync(fullPath, fileHunk.Hunks, ct);
                if (result.success)
                {
                    appliedFiles.Add(fileHunk.FilePath);
                }
                else
                {
                    failedHunks.Add(new { file = fileHunk.FilePath, reason = result.error });
                }
            }
            catch (Exception ex)
            {
                failedHunks.Add(new { file = fileHunk.FilePath, reason = ex.Message });
            }
        }

        if (failedHunks.Count > 0)
        {
            return ToolResult.Failure(
                $"Patch partially applied. Failed hunks: {failedHunks.Count}",
                JsonSerializer.Serialize(failedHunks));
        }

        return ToolResult.Success(new { appliedFiles });
    }

    private List<FilePatch> ParseUnifiedDiff(string patch)
    {
        // Implementation: parse unified diff format
        // Returns list of file patches with their hunks
        throw new NotImplementedException("Diff parser implementation needed");
    }

    private Task<(bool success, string? error)> ApplyHunksToFileAsync(
        string path, List<Hunk> hunks, CancellationToken ct)
    {
        // Implementation: apply hunks to file content
        throw new NotImplementedException("Hunk application implementation needed");
    }

    private record FilePatch(string FilePath, List<Hunk> Hunks);
    private record Hunk(int OldStart, int OldCount, int NewStart, int NewCount, List<string> Lines);
}
```

### 4.4 RunCommand Tool

```csharp
// src/Agent.Tools/RunCommandTool.cs
namespace Agent.Tools;

public class RunCommandTool : ITool
{
    public string Name => "RunCommand";
    public string Description => "Execute a command in the repository directory";
    public ToolPolicy Policy => new() { RequiresApproval = true, Risk = RiskLevel.High };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "exe": { "type": "string", "description": "Executable name or path" },
            "args": { "type": "array", "items": { "type": "string" } },
            "cwd": { "type": "string", "description": "Working directory (relative to repo)" },
            "timeoutSec": { "type": "integer", "default": 60 }
        },
        "required": ["exe"]
    }
    """;

    private const int MaxOutputLength = 50000;

    public async Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var exe = args.GetProperty("exe").GetString()!;
        var cmdArgs = args.TryGetProperty("args", out var a)
            ? a.EnumerateArray().Select(x => x.GetString()!).ToArray()
            : Array.Empty<string>();
        var timeoutSec = args.TryGetProperty("timeoutSec", out var t) ? t.GetInt32() : 60;

        var cwd = ctx.RepoRoot;
        if (args.TryGetProperty("cwd", out var cwdProp))
        {
            var relativeCwd = cwdProp.GetString()!;
            var resolvedCwd = ctx.Workspace.ResolvePath(relativeCwd);
            if (resolvedCwd == null)
            {
                return ToolResult.Failure($"Invalid working directory: {relativeCwd}");
            }
            cwd = resolvedCwd;
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var arg in cmdArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        // Filter environment variables
        FilterEnvironment(psi.Environment);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null && stdout.Length < MaxOutputLength)
            {
                stdout.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null && stderr.Length < MaxOutputLength)
            {
                stderr.AppendLine(e.Data);
            }
        };

        var stopwatch = Stopwatch.StartNew();

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            return ToolResult.Failure($"Command timed out after {timeoutSec}s");
        }

        stopwatch.Stop();

        var result = new
        {
            exitCode = process.ExitCode,
            durationMs = stopwatch.ElapsedMilliseconds,
            stdoutTruncated = stdout.Length >= MaxOutputLength,
            stderrTruncated = stderr.Length >= MaxOutputLength
        };

        if (process.ExitCode == 0)
        {
            return ToolResult.Success(result, stdout.ToString()) with
            {
                Stderr = stderr.Length > 0 ? stderr.ToString() : null
            };
        }

        return ToolResult.Failure($"Command exited with code {process.ExitCode}", stderr.ToString()) with
        {
            Stdout = stdout.ToString(),
            Data = result
        };
    }

    private void FilterEnvironment(IDictionary<string, string?> env)
    {
        var sensitiveKeys = new[] { "API_KEY", "SECRET", "PASSWORD", "TOKEN", "CREDENTIAL" };
        var keysToRemove = env.Keys
            .Where(k => sensitiveKeys.Any(s => k.Contains(s, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var key in keysToRemove)
        {
            env.Remove(key);
        }
    }
}
```

### 4.5 ListFiles Tool

```csharp
// src/Agent.Tools/ListFilesTool.cs
namespace Agent.Tools;

public class ListFilesTool : ITool
{
    public string Name => "ListFiles";
    public string Description => "List files in a directory matching a glob pattern";
    public ToolPolicy Policy => new() { IsReadOnly = true };

    public string InputSchema => """
    {
        "type": "object",
        "properties": {
            "glob": { "type": "string", "description": "Glob pattern (e.g., **/*.cs)" },
            "maxDepth": { "type": "integer", "default": 10 }
        },
        "required": ["glob"]
    }
    """;

    public Task<ToolResult> ExecuteAsync(JsonElement args, ToolContext ctx, CancellationToken ct)
    {
        var glob = args.GetProperty("glob").GetString()!;
        var maxDepth = args.TryGetProperty("maxDepth", out var d) ? d.GetInt32() : 10;

        var matcher = new Microsoft.Extensions.FileSystemGlobbing.Matcher();
        matcher.AddInclude(glob);

        var result = matcher.Execute(
            new Microsoft.Extensions.FileSystemGlobbing.Abstractions.DirectoryInfoWrapper(
                new DirectoryInfo(ctx.RepoRoot)));

        var files = result.Files
            .Select(f => f.Path)
            .Take(500) // Limit results
            .ToList();

        return Task.FromResult(ToolResult.Success(new
        {
            pattern = glob,
            count = files.Count,
            files
        }));
    }
}
```

---

## 5. Agent.Workspace Implementation

### 5.1 Workspace Interface

```csharp
// src/Agent.Workspace/IWorkspace.cs
namespace Agent.Workspace;

public interface IWorkspace
{
    string Root { get; }
    string? ResolvePath(string relativePath);
    bool IsPathSafe(string fullPath);
}

// src/Agent.Workspace/LocalWorkspace.cs
public class LocalWorkspace : IWorkspace
{
    public string Root { get; }
    private readonly string _normalizedRoot;

    public LocalWorkspace(string root)
    {
        Root = Path.GetFullPath(root);
        _normalizedRoot = NormalizePath(Root);
    }

    public string? ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        // Prevent absolute paths
        if (Path.IsPathRooted(relativePath))
            return null;

        var combined = Path.Combine(Root, relativePath);
        var fullPath = Path.GetFullPath(combined);

        return IsPathSafe(fullPath) ? fullPath : null;
    }

    public bool IsPathSafe(string fullPath)
    {
        var normalized = NormalizePath(fullPath);

        // Must be under root
        if (!normalized.StartsWith(_normalizedRoot, StringComparison.OrdinalIgnoreCase))
            return false;

        // Check for symlink escape (if path exists)
        if (File.Exists(fullPath) || Directory.Exists(fullPath))
        {
            var attributes = File.GetAttributes(fullPath);
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                // Resolve symlink target
                var target = Path.GetFullPath(fullPath);
                if (!NormalizePath(target).StartsWith(_normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        return true;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
```

---

## 6. Agent.Logging Implementation

### 6.1 Session Logger

```csharp
// src/Agent.Logging/ISessionLogger.cs
namespace Agent.Logging;

public interface ISessionLogger : IAsyncDisposable
{
    Task LogAsync<T>(T entry);
}

// src/Agent.Logging/JsonlSessionLogger.cs
public class JsonlSessionLogger : ISessionLogger
{
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonlSessionLogger(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _writer = new StreamWriter(filePath, append: true, Encoding.UTF8);
    }

    public async Task LogAsync<T>(T entry)
    {
        var line = JsonSerializer.Serialize(new
        {
            timestamp = DateTimeOffset.UtcNow,
            data = entry
        });

        await _lock.WaitAsync();
        try
        {
            await _writer.WriteLineAsync(line);
            await _writer.FlushAsync();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        _lock.Dispose();
    }
}
```

---

## 7. Agent.Cli Implementation

### 7.1 Entry Point

```csharp
// src/Agent.Cli/Program.cs
using System.CommandLine;

var rootCommand = new RootCommand("Local coding agent");

var repoOption = new Option<string>(
    "--repo",
    getDefaultValue: () => Environment.CurrentDirectory,
    description: "Repository root path");

var providerOption = new Option<string>(
    "--provider",
    getDefaultValue: () => "anthropic",
    description: "LLM provider (openai|anthropic)");

var modelOption = new Option<string>(
    "--model",
    getDefaultValue: () => "claude-sonnet-4-20250514",
    description: "Model name");

var autoApproveOption = new Option<bool>(
    "--yes",
    getDefaultValue: () => false,
    description: "Auto-approve all tool calls");

var sessionOption = new Option<string?>(
    "--session",
    description: "Session log file path");

var promptArgument = new Argument<string>(
    "prompt",
    description: "Task prompt for the agent");

rootCommand.AddOption(repoOption);
rootCommand.AddOption(providerOption);
rootCommand.AddOption(modelOption);
rootCommand.AddOption(autoApproveOption);
rootCommand.AddOption(sessionOption);
rootCommand.AddArgument(promptArgument);

rootCommand.SetHandler(async (repo, provider, model, autoApprove, session, prompt) =>
{
    var options = new AgentOptions
    {
        RepoRoot = Path.GetFullPath(repo),
        Model = model,
        MaxIterations = 20,
        MaxTokens = 4096,
        SystemPrompt = GetSystemPrompt()
    };

    var workspace = new LocalWorkspace(options.RepoRoot);
    options.Workspace = workspace;

    var modelProvider = CreateProvider(provider, model);
    var toolRegistry = CreateToolRegistry();
    var approvalService = new ConsoleApprovalService();
    var policyEngine = new DefaultPolicyEngine(new PolicyOptions { AutoApprove = autoApprove });

    var sessionPath = session ?? Path.Combine(
        options.RepoRoot, ".agent", $"session_{DateTime.Now:yyyyMMdd_HHmmss}.jsonl");
    var logger = new JsonlSessionLogger(sessionPath);

    var orchestrator = new AgentOrchestrator(
        modelProvider,
        toolRegistry,
        approvalService,
        policyEngine,
        logger,
        options);

    Console.WriteLine($"Agent started in: {options.RepoRoot}");
    Console.WriteLine($"Provider: {provider}, Model: {model}");
    Console.WriteLine($"Session log: {sessionPath}");
    Console.WriteLine();

    await orchestrator.RunAsync(prompt);

    await logger.DisposeAsync();

}, repoOption, providerOption, modelOption, autoApproveOption, sessionOption, promptArgument);

return await rootCommand.InvokeAsync(args);

static IModelProvider CreateProvider(string provider, string model)
{
    var httpClient = new HttpClient();

    return provider.ToLowerInvariant() switch
    {
        "anthropic" => new ClaudeProvider(
            httpClient,
            Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
                ?? throw new InvalidOperationException("ANTHROPIC_API_KEY not set")),
        "openai" => new OpenAIProvider(
            httpClient,
            Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? throw new InvalidOperationException("OPENAI_API_KEY not set")),
        _ => throw new ArgumentException($"Unknown provider: {provider}")
    };
}

static ToolRegistry CreateToolRegistry()
{
    var registry = new ToolRegistry();
    registry.Register(new ReadFileTool());
    registry.Register(new ListFilesTool());
    registry.Register(new SearchTextTool());
    registry.Register(new ApplyPatchTool());
    registry.Register(new RunCommandTool());
    return registry;
}

static string GetSystemPrompt()
{
    return """
    You are a coding assistant that helps developers with tasks in their repository.

    Available tools:
    - ReadFile: Read file contents
    - ListFiles: List files matching a pattern
    - SearchText: Search for text in files
    - ApplyPatch: Apply unified diff patches
    - RunCommand: Execute shell commands

    Guidelines:
    1. Always read relevant files before making changes
    2. Use SearchText to find code locations
    3. Generate unified diff patches for changes
    4. Run tests after making changes
    5. Keep changes minimal and focused
    """;
}
```

---

## 8. Testing

### 8.1 Unit Test Examples

```csharp
// tests/Agent.Core.Tests/WorkspaceTests.cs
public class WorkspaceTests
{
    private readonly string _testRoot;
    private readonly LocalWorkspace _workspace;

    public WorkspaceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "agent_test_" + Guid.NewGuid());
        Directory.CreateDirectory(_testRoot);
        _workspace = new LocalWorkspace(_testRoot);
    }

    [Fact]
    public void ResolvePath_ValidRelative_ReturnsFullPath()
    {
        var result = _workspace.ResolvePath("src/file.cs");
        result.Should().Be(Path.Combine(_testRoot, "src", "file.cs"));
    }

    [Fact]
    public void ResolvePath_TraversalAttempt_ReturnsNull()
    {
        var result = _workspace.ResolvePath("../../../etc/passwd");
        result.Should().BeNull();
    }

    [Fact]
    public void ResolvePath_AbsolutePath_ReturnsNull()
    {
        var result = _workspace.ResolvePath("/etc/passwd");
        result.Should().BeNull();
    }
}

// tests/Agent.Tools.Tests/ReadFileToolTests.cs
public class ReadFileToolTests
{
    [Fact]
    public async Task ExecuteAsync_ExistingFile_ReturnsContent()
    {
        // Arrange
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var testFile = Path.Combine(tempDir, "test.txt");
        await File.WriteAllTextAsync(testFile, "line1\nline2\nline3");

        var workspace = new LocalWorkspace(tempDir);
        var tool = new ReadFileTool();
        var args = JsonDocument.Parse("""{"path": "test.txt"}""").RootElement;
        var context = new ToolContext
        {
            RepoRoot = tempDir,
            Workspace = workspace,
            ApprovalService = Mock.Of<IApprovalService>()
        };

        // Act
        var result = await tool.ExecuteAsync(args, context);

        // Assert
        result.Ok.Should().BeTrue();
        var data = (JsonElement)result.Data!;
        data.GetProperty("content").GetString().Should().Contain("line1");

        // Cleanup
        Directory.Delete(tempDir, true);
    }
}
```

---

## 9. Configuration Files

### 9.1 Directory.Build.props

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

### 9.2 .editorconfig

```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = lf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.{cs,csx}]
dotnet_sort_system_directives_first = true
csharp_using_directive_placement = outside_namespace
csharp_style_namespace_declarations = file_scoped:suggestion
```

---

## 10. Running the Agent

```bash
# Set API key
export ANTHROPIC_API_KEY=your_key_here

# Build
dotnet build

# Run
dotnet run --project src/Agent.Cli -- --repo /path/to/repo "Fix the bug in UserService.cs"

# With auto-approve (use with caution)
dotnet run --project src/Agent.Cli -- --repo /path/to/repo --yes "Add unit tests for Calculator.cs"
```

---

## 11. Next Steps

After completing the MVP implementation:

1. **OpenAI Provider**: Implement `Agent.Llm.OpenAI` following the same pattern as Claude
2. **Parallel Tool Calls**: Extend orchestrator to handle multiple simultaneous tool calls
3. **Context Compression**: Implement summarization for long conversations
4. **Git Integration**: Add `GitStatus`, `GitDiff`, `GitCommit` tools
5. **Test Runner**: Add language-specific test execution profiles
6. **Schema Validation**: Integrate JSON Schema validation library
