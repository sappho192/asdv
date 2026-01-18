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

    public async Task RunAsync(
        string userPrompt,
        List<ChatMessage>? messages = null,
        CancellationToken ct = default)
    {
        messages ??= new List<ChatMessage>();
        var userMessage = new UserMessage(userPrompt);
        messages.Add(userMessage);

        await _logger.LogAsync(new { type = "user_prompt", content = userPrompt });
        await LogMessageAsync(userMessage);

        var exhausted = true;
        for (int iteration = 0; iteration < _options.MaxIterations; iteration++)
        {
            var request = BuildRequest(messages);
            var pendingToolCalls = new List<ToolCallReady>();
            var textBuffer = new StringBuilder();
            var formatter = new TextStreamFormatter();

            var completedStop = false;
            string? stopReason = null;
            string? errorDetails = null;
            await foreach (var evt in _provider.StreamAsync(request, ct))
            {
                await _logger.LogAsync(new { type = "event", eventType = evt.GetType().Name, data = evt });

                switch (evt)
                {
                    case TextDelta delta:
                        var rendered = formatter.Format(delta.Text);
                        Console.Write(rendered);
                        textBuffer.Append(delta.Text);
                        break;

                    case ToolCallStarted started:
                        Console.Write(formatter.Flush());
                        Console.WriteLine();
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.Write($"[tool] {started.ToolName}");
                        Console.ResetColor();
                        break;

                    case ToolCallReady ready:
                        Console.WriteLine($" args={TruncateJson(ready.ArgsJson, 100)}");
                        pendingToolCalls.Add(ready);
                        break;

                    case TraceEvent trace when trace.Kind == "error":
                        errorDetails = trace.Data;
                        Console.Write(formatter.Flush());
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[Provider error] {trace.Data}");
                        Console.ResetColor();
                        break;

                    case ResponseCompleted completed:
                        Console.Write(formatter.Flush());
                        Console.WriteLine();
                        stopReason = completed.StopReason;
                        completedStop = IsCompletionStopReason(completed.StopReason);
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
                messages.Add(assistantMessage);
            }
            if (assistantMessage != null)
            {
                await LogMessageAsync(assistantMessage);
            }

            if (pendingToolCalls.Count == 0 && completedStop)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[Agent completed]");
                Console.ResetColor();
                return;
            }

            if (pendingToolCalls.Count == 0 && textBuffer.Length == 0 && !completedStop)
            {
                var reason = stopReason ?? "unknown";
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[No response] stop_reason={reason}");
                if (!string.IsNullOrWhiteSpace(errorDetails))
                {
                    Console.WriteLine($"[Provider error] {errorDetails}");
                }
                Console.ResetColor();
                return;
            }

            if (pendingToolCalls.Count > 0)
            {
                foreach (var toolCall in pendingToolCalls)
                {
                    var result = await ExecuteToolCallAsync(toolCall, ct);
                    var toolMessage = new ToolResultMessage(toolCall.CallId, toolCall.ToolName, result);
                    messages.Add(toolMessage);
                    await LogMessageAsync(toolMessage);

                    PrintToolResult(toolCall.ToolName, result);
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
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Max iterations reached]");
            Console.ResetColor();
        }
    }

    private async Task<ToolResult> ExecuteToolCallAsync(ToolCallReady toolCall, CancellationToken ct)
    {
        var tool = _toolRegistry.GetTool(toolCall.ToolName);
        if (tool == null)
        {
            return ToolResult.Failure($"Unknown tool: {toolCall.ToolName}");
        }

        var decision = await _policyEngine.EvaluateAsync(tool, toolCall.ArgsJson);
        if (decision == PolicyDecision.Denied)
        {
            return ToolResult.Failure("Tool execution denied by policy");
        }

        if (decision == PolicyDecision.RequiresApproval)
        {
            var approved = await _approvalService.RequestApprovalAsync(
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

    private static void PrintToolResult(string toolName, ToolResult result)
    {
        if (result.Ok)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  [{toolName}] OK");
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  [{toolName}] FAILED: {result.Diagnostics?.FirstOrDefault()?.Message}");
        }
        Console.ResetColor();

        if (!string.IsNullOrEmpty(result.Stdout))
        {
            var preview = TruncateString(result.Stdout, 200);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"  stdout: {preview}");
            Console.ResetColor();
        }
    }

    private static string TruncateJson(string json, int maxLength)
    {
        if (json.Length <= maxLength) return json;
        return json[..maxLength] + "...";
    }

    private static string TruncateString(string str, int maxLength)
    {
        var singleLine = str.Replace("\r", "").Replace("\n", " ");
        if (singleLine.Length <= maxLength) return singleLine;
        return singleLine[..maxLength] + "...";
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

    private sealed class TextStreamFormatter
    {
        private bool _inCodeBlock;
        private int _backtickRun;
        private bool _pendingBackslash;

        public string Format(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var output = new StringBuilder(text.Length);

            foreach (var ch in text)
            {
                if (_pendingBackslash)
                {
                    if (ch == 'n' && !_inCodeBlock)
                    {
                        output.AppendLine();
                    }
                    else
                    {
                        output.Append('\\');
                        ProcessChar(ch, output);
                    }

                    _pendingBackslash = false;
                    continue;
                }

                if (ch == '\\')
                {
                    _pendingBackslash = true;
                    continue;
                }

                ProcessChar(ch, output);
            }

            return output.ToString();
        }

        public string Flush()
        {
            var output = new StringBuilder();

            if (_pendingBackslash)
            {
                output.Append('\\');
                _pendingBackslash = false;
            }

            FlushBackticks(output);
            return output.ToString();
        }

        private void ProcessChar(char ch, StringBuilder output)
        {
            if (ch == '`')
            {
                _backtickRun++;
                return;
            }

            FlushBackticks(output);
            output.Append(ch);
        }

        private void FlushBackticks(StringBuilder output)
        {
            if (_backtickRun == 0)
            {
                return;
            }

            var remaining = _backtickRun;
            while (remaining >= 3)
            {
                _inCodeBlock = !_inCodeBlock;
                output.Append("```");
                remaining -= 3;
            }

            if (remaining > 0)
            {
                output.Append('`', remaining);
            }

            _backtickRun = 0;
        }
    }
}
