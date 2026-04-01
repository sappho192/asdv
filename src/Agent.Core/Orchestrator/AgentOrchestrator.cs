using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Agent.Core.Approval;
using Agent.Core.Events;
using Agent.Core.Logging;
using Agent.Core.Messages;
using Agent.Core.Policy;
using Agent.Core.Providers;
using Agent.Core.Tools;

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

    public async IAsyncEnumerable<AgentEvent> RunStreamAsync(
        string userPrompt,
        List<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var userMessage = new UserMessage(userPrompt);
        messages.Add(userMessage);

        await _logger.LogAsync(new { type = "user_prompt", content = userPrompt });
        await LogMessageAsync(userMessage);

        for (int iteration = 0; iteration < _options.MaxIterations; iteration++)
        {
            yield return new IterationStarted(iteration, _options.MaxIterations);

            var request = BuildRequest(messages);
            var pendingToolCalls = new List<ToolCallReady>();
            var textBuffer = new StringBuilder();
            var completedStop = false;
            string? stopReason = null;
            string? errorDetails = null;

            await foreach (var evt in _provider.StreamAsync(request, ct))
            {
                await _logger.LogAsync(new { type = "event", eventType = evt.GetType().Name, data = evt });

                switch (evt)
                {
                    case TextDelta:
                        textBuffer.Append(((TextDelta)evt).Text);
                        yield return evt;
                        break;

                    case ToolCallStarted:
                        yield return evt;
                        break;

                    case ToolCallArgsDelta:
                        yield return evt;
                        break;

                    case ToolCallReady ready:
                        pendingToolCalls.Add(ready);
                        yield return evt;
                        break;

                    case TraceEvent trace when trace.Kind == "error":
                        errorDetails = trace.Data;
                        yield return evt;
                        break;

                    case ResponseCompleted completed:
                        stopReason = completed.StopReason;
                        completedStop = IsCompletionStopReason(completed.StopReason);
                        yield return evt;
                        break;

                    default:
                        yield return evt;
                        break;
                }
            }

            // Build assistant message from accumulated text and tool calls
            AssistantMessage? assistantMessage = null;
            if (textBuffer.Length > 0 || pendingToolCalls.Count > 0)
            {
                assistantMessage = new AssistantMessage
                {
                    Content = textBuffer.Length > 0 ? textBuffer.ToString() : null,
                    ToolCalls = pendingToolCalls.Count > 0
                        ? pendingToolCalls.Select(tc => new ToolCall(tc.CallId, tc.ToolName, tc.ArgsJson)).ToList()
                        : null
                };
                messages.Add(assistantMessage);
            }
            if (assistantMessage != null)
            {
                await LogMessageAsync(assistantMessage);
            }

            yield return new AssistantMessageCompleted(
                textBuffer.Length > 0 ? textBuffer.ToString() : null,
                pendingToolCalls.Count);

            // Check termination conditions
            if (pendingToolCalls.Count == 0 && completedStop)
            {
                yield return new AgentCompleted(stopReason ?? "end_turn");
                yield break;
            }

            if (pendingToolCalls.Count == 0 && textBuffer.Length == 0 && !completedStop)
            {
                var reason = stopReason ?? "unknown";
                var msg = $"No response. stop_reason={reason}";
                if (!string.IsNullOrWhiteSpace(errorDetails))
                    msg += $". error={errorDetails}";
                yield return new AgentError(msg);
                yield break;
            }

            if (pendingToolCalls.Count == 0)
            {
                // Text-only response, no tool calls, not a completion stop — break loop
                yield break;
            }

            // Execute tool calls
            foreach (var toolCall in pendingToolCalls)
            {
                yield return new ToolExecutionStarted(toolCall.CallId, toolCall.ToolName, toolCall.ArgsJson);

                var result = await ExecuteToolCallWithEventsAsync(toolCall, ct);

                var toolMessage = new ToolResultMessage(toolCall.CallId, toolCall.ToolName, result.Result);
                messages.Add(toolMessage);
                await LogMessageAsync(toolMessage);

                yield return new ToolExecutionCompleted(toolCall.CallId, toolCall.ToolName, result.Result);
            }
        }

        yield return new MaxIterationsReached(_options.MaxIterations);
    }

    private async Task<ToolExecutionResult> ExecuteToolCallWithEventsAsync(
        ToolCallReady toolCall, CancellationToken ct)
    {
        var executionResult = new ToolExecutionResult();

        var tool = _toolRegistry.GetTool(toolCall.ToolName);
        if (tool == null)
        {
            executionResult.Result = ToolResult.Failure($"Unknown tool: {toolCall.ToolName}");
            return executionResult;
        }

        var decision = await _policyEngine.EvaluateAsync(tool, toolCall.ArgsJson);
        if (decision == PolicyDecision.Denied)
        {
            executionResult.Result = ToolResult.Failure("Tool execution denied by policy");
            return executionResult;
        }

        if (decision == PolicyDecision.RequiresApproval)
        {
            executionResult.ApprovalRequested = true;
            var approved = await _approvalService.RequestApprovalAsync(
                tool.Name, toolCall.ArgsJson, toolCall.CallId, ct);
            executionResult.ApprovalResolved = true;
            executionResult.ApprovalGranted = approved;

            if (!approved)
            {
                executionResult.Result = ToolResult.Failure("User denied approval");
                return executionResult;
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

            executionResult.Result = await tool.ExecuteAsync(args, context, ct);
            await _logger.LogAsync(new
            {
                type = "tool_result",
                callId = toolCall.CallId,
                tool = toolCall.ToolName,
                ok = executionResult.Result.Ok,
                diagnostics = executionResult.Result.Diagnostics
            });

            return executionResult;
        }
        catch (Exception ex)
        {
            executionResult.Result = ToolResult.Failure($"Tool execution failed: {ex.Message}");
            return executionResult;
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

    private static bool IsCompletionStopReason(string? stopReason)
    {
        return stopReason == "end_turn" || stopReason == "stop";
    }

    private Task LogMessageAsync(ChatMessage message)
    {
        return message switch
        {
            UserMessage userMessage => _logger.LogAsync(new
            {
                type = "message",
                role = userMessage.Role,
                content = userMessage.Content
            }),
            AssistantMessage assistantMessage => _logger.LogAsync(new
            {
                type = "message",
                role = assistantMessage.Role,
                content = assistantMessage.Content,
                toolCalls = assistantMessage.ToolCalls?.Select(tc => new
                {
                    callId = tc.CallId,
                    name = tc.Name,
                    argsJson = tc.ArgsJson
                })
            }),
            ToolResultMessage toolResultMessage => _logger.LogAsync(new
            {
                type = "message",
                role = toolResultMessage.Role,
                callId = toolResultMessage.CallId,
                toolName = toolResultMessage.ToolName,
                result = toolResultMessage.Result
            }),
            _ => _logger.LogAsync(new { type = "message", role = message.Role })
        };
    }

    private sealed class ToolExecutionResult
    {
        public ToolResult Result { get; set; } = ToolResult.Failure("Not executed");
        public bool ApprovalRequested { get; set; }
        public bool ApprovalResolved { get; set; }
        public bool ApprovalGranted { get; set; }
    }
}
