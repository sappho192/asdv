using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Agent.Core.Events;
using Agent.Core.Messages;
using Agent.Core.Providers;
using Agent.Core.Tools;

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

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            yield return new TraceEvent("error", $"HTTP {(int)response.StatusCode}: {errorBody}");
            yield return new ResponseCompleted("error", null);
            yield break;
        }

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

            JsonDocument? doc = null;
            bool parseError = false;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch
            {
                parseError = true;
            }

            if (parseError)
            {
                yield return new TraceEvent("parse_error", json);
                continue;
            }

            using (doc)
            {
                var evt = doc!.RootElement;

                if (!evt.TryGetProperty("type", out var typeEl))
                {
                    yield return new TraceEvent("unknown", json);
                    continue;
                }

                var eventType = typeEl.GetString();

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
                        var stopReason = "unknown";
                        if (evt.TryGetProperty("delta", out var messageDelta) &&
                            messageDelta.TryGetProperty("stop_reason", out var stopReasonEl))
                        {
                            stopReason = stopReasonEl.GetString() ?? "unknown";
                        }

                        int outputTokens = 0;
                        if (evt.TryGetProperty("usage", out var deltaUsage) &&
                            deltaUsage.TryGetProperty("output_tokens", out var outputTokensEl))
                        {
                            outputTokens = outputTokensEl.GetInt32();
                        }

                        yield return new ResponseCompleted(
                            stopReason,
                            usage != null ? usage with { OutputTokens = outputTokens } : null);
                        break;

                    case "message_stop":
                        // Final event, already handled by message_delta
                        break;

                    case "ping":
                        // Keep-alive, ignore
                        break;

                    default:
                        yield return new TraceEvent(eventType ?? "unknown", json);
                        break;
                }
            }
        }
    }

    private HttpRequestMessage BuildRequest(ModelRequest request)
    {
        var tools = request.Tools?.Select(t =>
        {
            JsonElement inputSchema;
            try
            {
                inputSchema = JsonDocument.Parse(t.InputSchema).RootElement;
            }
            catch
            {
                inputSchema = JsonDocument.Parse("{}").RootElement;
            }

            return new
            {
                name = t.Name,
                description = t.Description,
                input_schema = inputSchema
            };
        }).ToList();

        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["max_tokens"] = request.MaxTokens ?? 4096,
            ["messages"] = ConvertMessages(request.Messages),
            ["stream"] = true
        };

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            body["system"] = request.SystemPrompt;
        }

        if (tools != null && tools.Count > 0)
        {
            body["tools"] = tools;
        }

        if (request.Temperature.HasValue)
        {
            body["temperature"] = request.Temperature.Value;
        }

        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, BaseUrl)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Add("x-api-key", _apiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        return httpRequest;
    }

    private static object[] ConvertMessages(IReadOnlyList<ChatMessage> messages)
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
                            JsonElement inputElement;
                            try
                            {
                                inputElement = JsonDocument.Parse(tc.ArgsJson).RootElement;
                            }
                            catch
                            {
                                inputElement = JsonDocument.Parse("{}").RootElement;
                            }

                            content.Add(new
                            {
                                type = "tool_use",
                                id = tc.CallId,
                                name = tc.Name,
                                input = inputElement
                            });
                        }
                    }
                    if (content.Count > 0)
                    {
                        result.Add(new { role = "assistant", content });
                    }
                    break;

                case ToolResultMessage toolResult:
                    var resultContent = toolResult.Result.Ok
                        ? SerializeToolData(toolResult.Result.Data, toolResult.Result.Stdout)
                        : toolResult.Result.Stderr ?? toolResult.Result.Diagnostics?.FirstOrDefault()?.Message ?? "Error";

                    var toolContent = new[]
                    {
                        new
                        {
                            type = "tool_result",
                            tool_use_id = toolResult.CallId,
                            content = resultContent,
                            is_error = !toolResult.Result.Ok
                        }
                    };
                    result.Add(new { role = "user", content = toolContent });
                    break;
            }
        }

        return result.ToArray();
    }

    private static string SerializeToolData(object? data, string? stdout)
    {
        if (data != null)
        {
            try
            {
                return JsonSerializer.Serialize(data, new JsonSerializerOptions
                {
                    WriteIndented = false,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
            }
            catch
            {
                // Fall through to stdout
            }
        }

        return stdout ?? "OK";
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
        public string ArgsJson => _args.Length > 0 ? _args.ToString() : "{}";
    }
}
