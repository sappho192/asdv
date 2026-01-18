using System.Text;
using System.Text.Json;
using Agent.Core.Messages;
using Agent.Core.Policy;
using Agent.Core.Providers;
using Agent.Core.Tools;
using Agent.Server.Models;
using CoreEvents = Agent.Core.Events;

namespace Agent.Server.Services;

public sealed class SessionRunner
{
    public async Task RunAsync(SessionRuntime session, string userPrompt, CancellationToken ct)
    {
        await session.RunLock.WaitAsync(ct);
        try
        {
            await RunInternalAsync(session, userPrompt, ct);
        }
        finally
        {
            session.RunLock.Release();
        }
    }

    private async Task RunInternalAsync(SessionRuntime session, string userPrompt, CancellationToken ct)
    {
        var events = session.Events.Writer;
        session.ApprovalService.AttachChannel(events);

        var userMessage = new UserMessage(userPrompt);
        session.Messages.Add(userMessage);
        await session.Logger.LogAsync(new { type = "user_prompt", content = userPrompt });
        await LogMessageAsync(session, userMessage);

        var exhausted = true;
        for (int iteration = 0; iteration < session.Options.MaxIterations; iteration++)
        {
            var request = new ModelRequest
            {
                Model = session.Options.Model,
                SystemPrompt = session.Options.SystemPrompt,
                Messages = session.Messages,
                Tools = session.ToolRegistry.GetToolDefinitions(),
                MaxTokens = session.Options.MaxTokens,
                Temperature = session.Options.Temperature
            };

            var pendingToolCalls = new List<CoreEvents.ToolCallReady>();
            var textBuffer = new StringBuilder();
            var completedStop = false;
            string? stopReason = null;
            string? errorDetails = null;

            await foreach (var evt in session.Provider.StreamAsync(request, ct))
            {
                await session.Logger.LogAsync(new { type = "event", eventType = evt.GetType().Name, data = evt });

                switch (evt)
                {
                    case CoreEvents.TextDelta delta:
                        textBuffer.Append(delta.Text);
                        events.TryWrite(new TextDeltaEvent(delta.Text));
                        break;

                    case CoreEvents.ToolCallReady ready:
                        pendingToolCalls.Add(ready);
                        events.TryWrite(new ToolCallEvent(ready.CallId, ready.ToolName, ready.ArgsJson));
                        break;

                    case CoreEvents.TraceEvent trace when trace.Kind == "error":
                        errorDetails = trace.Data;
                        events.TryWrite(new Agent.Server.Models.TraceEvent(trace.Kind, trace.Data));
                        break;

                    case CoreEvents.ResponseCompleted completed:
                        stopReason = completed.StopReason;
                        completedStop = IsCompletionStopReason(completed.StopReason);
                        events.TryWrite(new CompletedEvent(completed.StopReason));
                        break;
                }
            }

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
                session.Messages.Add(assistantMessage);
            }
            if (assistantMessage != null)
            {
                await LogMessageAsync(session, assistantMessage);
            }

            if (pendingToolCalls.Count == 0 && completedStop)
            {
                return;
            }

            if (pendingToolCalls.Count == 0 && textBuffer.Length == 0 && !completedStop)
            {
                var reason = stopReason ?? "unknown";
                events.TryWrite(new ErrorEvent($"No response. stop_reason={reason}. error={errorDetails}"));
                return;
            }

            if (pendingToolCalls.Count > 0)
            {
                foreach (var toolCall in pendingToolCalls)
                {
                    var result = await ExecuteToolCallAsync(session, toolCall, ct);
                    var toolMessage = new ToolResultMessage(toolCall.CallId, toolCall.ToolName, result);
                    session.Messages.Add(toolMessage);
                    await LogMessageAsync(session, toolMessage);
                    events.TryWrite(new Agent.Server.Models.ToolResultEvent(toolCall.CallId, toolCall.ToolName, result));
                }
            }
            else
            {
                exhausted = false;
                break;
            }
        }

        if (exhausted)
        {
            events.TryWrite(new ErrorEvent("Max iterations reached."));
        }
    }

    private async Task<ToolResult> ExecuteToolCallAsync(
        SessionRuntime session,
        CoreEvents.ToolCallReady toolCall,
        CancellationToken ct)
    {
        var tool = session.ToolRegistry.GetTool(toolCall.ToolName);
        if (tool == null)
        {
            return ToolResult.Failure($"Unknown tool: {toolCall.ToolName}");
        }

        var decision = await session.PolicyEngine.EvaluateAsync(tool, toolCall.ArgsJson);
        if (decision == PolicyDecision.Denied)
        {
            return ToolResult.Failure("Tool execution denied by policy");
        }

        if (decision == PolicyDecision.RequiresApproval)
        {
            var approved = await session.ApprovalService.RequestApprovalAsync(
                tool.Name, toolCall.ArgsJson, toolCall.CallId, ct);
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
                RepoRoot = session.Options.RepoRoot,
                Workspace = session.Options.Workspace,
                ApprovalService = session.ApprovalService
            };

            var result = await tool.ExecuteAsync(args, context, ct);
            await session.Logger.LogAsync(new
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

    private static bool IsCompletionStopReason(string? stopReason)
    {
        return stopReason == "end_turn" || stopReason == "stop";
    }

    private static Task LogMessageAsync(SessionRuntime session, ChatMessage message)
    {
        return message switch
        {
            UserMessage userMessage => session.Logger.LogAsync(new
            {
                type = "message",
                role = userMessage.Role,
                content = userMessage.Content
            }),
            AssistantMessage assistantMessage => session.Logger.LogAsync(new
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
            ToolResultMessage toolResultMessage => session.Logger.LogAsync(new
            {
                type = "message",
                role = toolResultMessage.Role,
                callId = toolResultMessage.CallId,
                toolName = toolResultMessage.ToolName,
                result = toolResultMessage.Result
            }),
            _ => session.Logger.LogAsync(new { type = "message", role = message.Role })
        };
    }
}
