using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Agent.Core.Events;
using Agent.Core.Messages;
using Agent.Core.Providers;
using Agent.Core.Tools;

namespace Agent.Llm.OpenAI;

public class OpenAIProvider : IModelProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    public string Name => "openai";

    public OpenAIProvider(HttpClient httpClient, string apiKey, string? baseUrl = null)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _baseUrl = baseUrl ?? "https://api.openai.com/v1";
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
        string? finishReason = null;

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(line)) continue;
            if (!line.StartsWith("data: ")) continue;

            var json = line[6..];
            if (json == "[DONE]")
            {
                break;
            }

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
                var root = doc!.RootElement;

                // Handle usage information
                if (root.TryGetProperty("usage", out var usageEl) && usageEl.ValueKind != JsonValueKind.Null)
                {
                    var promptTokens = usageEl.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                    var completionTokens = usageEl.TryGetProperty("completion_tokens", out var ct2) ? ct2.GetInt32() : 0;
                    usage = new UsageInfo(promptTokens, completionTokens);
                }

                // Process choices
                if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                {
                    continue;
                }

                var choice = choices[0];

                // Check finish reason
                if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind != JsonValueKind.Null)
                {
                    finishReason = fr.GetString();
                }

                // Process delta
                if (!choice.TryGetProperty("delta", out var delta))
                {
                    continue;
                }

                // Text content
                if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
                {
                    var text = contentEl.GetString();
                    if (!string.IsNullOrEmpty(text))
                    {
                        yield return new TextDelta(text);
                    }
                }

                // Tool calls
                if (delta.TryGetProperty("tool_calls", out var toolCalls))
                {
                    foreach (var toolCall in toolCalls.EnumerateArray())
                    {
                        if (!toolCall.TryGetProperty("index", out var indexEl))
                            continue;

                        var index = indexEl.GetInt32();

                        // New tool call
                        if (toolCall.TryGetProperty("id", out var idEl) && idEl.ValueKind != JsonValueKind.Null)
                        {
                            var callId = idEl.GetString()!;
                            if (toolCall.TryGetProperty("function", out var function) &&
                                function.ValueKind != JsonValueKind.Null &&
                                function.TryGetProperty("name", out var nameEl) &&
                                nameEl.ValueKind != JsonValueKind.Null)
                            {
                                var toolName = nameEl.GetString()!;
                                toolCallBuffers[index] = new ToolCallBuffer(callId, toolName);
                                yield return new ToolCallStarted(callId, toolName);
                            }
                        }

                        // Tool call arguments delta
                        if (toolCall.TryGetProperty("function", out var funcEl) &&
                            funcEl.ValueKind != JsonValueKind.Null &&
                            funcEl.TryGetProperty("arguments", out var argsEl) &&
                            argsEl.ValueKind != JsonValueKind.Null)
                        {
                            var argsDelta = argsEl.GetString();
                            if (!string.IsNullOrEmpty(argsDelta) && toolCallBuffers.TryGetValue(index, out var buffer))
                            {
                                buffer.AppendArgs(argsDelta);
                                yield return new ToolCallArgsDelta(buffer.CallId, argsDelta);
                            }
                        }
                    }
                }
            }
        }

        // Emit ToolCallReady for all accumulated tool calls
        foreach (var buffer in toolCallBuffers.Values)
        {
            yield return new ToolCallReady(buffer.CallId, buffer.ToolName, buffer.ArgsJson);
        }

        // Emit completion event
        yield return new ResponseCompleted(
            finishReason ?? "stop",
            usage);
    }

    private HttpRequestMessage BuildRequest(ModelRequest request)
    {
        var messages = ConvertMessages(request);

        var body = new Dictionary<string, object?>
        {
            ["model"] = request.Model,
            ["messages"] = messages,
            ["stream"] = true,
            ["stream_options"] = new { include_usage = true }
        };

        if (request.MaxTokens.HasValue)
        {
            body["max_completion_tokens"] = request.MaxTokens.Value;
        }

        if (request.Temperature.HasValue)
        {
            body["temperature"] = request.Temperature.Value;
        }

        if (request.Tools != null && request.Tools.Count > 0)
        {
            body["tools"] = request.Tools.Select(t =>
            {
                JsonNode? parameters;
                try
                {
                    // Parse to JsonNode to avoid JsonElement serialization issues
                    parameters = JsonNode.Parse(t.InputSchema);
                }
                catch
                {
                    parameters = JsonNode.Parse("{}");
                }

                return new
                {
                    type = "function",
                    function = new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters
                    }
                };
            }).ToList();
        }

        var json = JsonSerializer.Serialize(body, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Add("Authorization", $"Bearer {_apiKey}");

        return httpRequest;
    }

    private static List<object> ConvertMessages(ModelRequest request)
    {
        var messages = new List<object>();

        // Add system message if present
        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new { role = "system", content = request.SystemPrompt });
        }

        foreach (var msg in request.Messages)
        {
            switch (msg)
            {
                case UserMessage user:
                    messages.Add(new { role = "user", content = user.Content });
                    break;

                case AssistantMessage assistant:
                    if (assistant.ToolCalls != null && assistant.ToolCalls.Count > 0)
                    {
                        var toolCalls = assistant.ToolCalls.Select(tc => new
                        {
                            id = tc.CallId,
                            type = "function",
                            function = new
                            {
                                name = tc.Name,
                                arguments = tc.ArgsJson
                            }
                        }).ToList();

                        messages.Add(new
                        {
                            role = "assistant",
                            content = assistant.Content,
                            tool_calls = toolCalls
                        });
                    }
                    else
                    {
                        messages.Add(new { role = "assistant", content = assistant.Content ?? "" });
                    }
                    break;

                case ToolResultMessage toolResult:
                    var resultContent = toolResult.Result.Ok
                        ? SerializeToolData(toolResult.Result.Data, toolResult.Result.Stdout)
                        : toolResult.Result.Stderr ?? toolResult.Result.Diagnostics?.FirstOrDefault()?.Message ?? "Error";

                    messages.Add(new
                    {
                        role = "tool",
                        tool_call_id = toolResult.CallId,
                        content = resultContent
                    });
                    break;
            }
        }

        return messages;
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
