using Tiktoken;
using Agent.Core.Messages;
using ChatMessage = Agent.Core.Messages.ChatMessage;

namespace Agent.Core.Session;

public static class TokenEstimator
{
    private static readonly Encoder _encoder = ModelToEncoder.For("cl100k_base");

    /// <summary>
    /// Estimate token count for a string using TikToken cl100k_base encoding.
    /// Works reasonably well across providers including CJK text.
    /// </summary>
    public static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;

        return _encoder.CountTokens(text);
    }

    /// <summary>
    /// Estimate total token count for a list of messages.
    /// </summary>
    public static long EstimateTokens(IReadOnlyList<ChatMessage> messages)
    {
        long total = 0;
        foreach (var message in messages)
        {
            // Per-message overhead (~4 tokens for role/separators)
            total += 4;

            switch (message)
            {
                case UserMessage user:
                    total += EstimateTokens(user.Content);
                    break;
                case AssistantMessage assistant:
                    if (assistant.Content != null)
                        total += EstimateTokens(assistant.Content);
                    if (assistant.ToolCalls != null)
                    {
                        foreach (var tc in assistant.ToolCalls)
                        {
                            total += EstimateTokens(tc.Name) + EstimateTokens(tc.ArgsJson);
                        }
                    }
                    break;
                case ToolResultMessage toolResult:
                    var resultText = toolResult.Result.Ok
                        ? toolResult.Result.Stdout ?? ""
                        : toolResult.Result.Stderr ?? "";
                    total += EstimateTokens(resultText);
                    break;
            }
        }
        return total;
    }

    /// <summary>
    /// Format a token budget display string.
    /// </summary>
    public static string GetBudgetDisplay(SessionState state)
    {
        var inputTokens = state.EstimatedInputTokens;
        var outputTokens = state.EstimatedOutputTokens;

        if (state.MaxContextTokens.HasValue && state.MaxContextTokens > 0)
        {
            var max = state.MaxContextTokens.Value;
            var pct = (double)inputTokens / max * 100;
            return $"~{FormatCount(inputTokens)} / {FormatCount(max)} ({pct:F0}%)";
        }

        var total = inputTokens + outputTokens;
        if (total > 0)
            return $"~{FormatCount(total)} tokens (in: {FormatCount(inputTokens)}, out: {FormatCount(outputTokens)})";

        return "";
    }

    private static string FormatCount(long count)
    {
        return count >= 1000 ? $"{count / 1000.0:F1}k" : count.ToString();
    }
}
