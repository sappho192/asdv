using Agent.Core.Messages;

namespace Agent.Core.Session;

public enum CompactionStrategy
{
    SlidingWindow,
    ModelSummary
}

public static class ContextCompactor
{
    /// <summary>
    /// Compact messages to fit within a token budget using SlidingWindow strategy.
    /// Preserves the first user message (task context) and fills remaining budget
    /// with turns from the end, using TikToken for token counting.
    /// </summary>
    public static List<ChatMessage> CompactSlidingWindow(
        List<ChatMessage> messages,
        long targetTokenBudget)
    {
        if (messages.Count == 0)
            return messages;

        // Always keep the first user message for task context
        ChatMessage? firstUserMessage = null;
        int firstUserIdx = -1;
        for (int i = 0; i < messages.Count; i++)
        {
            if (messages[i] is UserMessage)
            {
                firstUserMessage = messages[i];
                firstUserIdx = i;
                break;
            }
        }

        if (firstUserMessage == null)
            return messages; // No user message found, can't compact meaningfully

        var firstUserTokens = TokenEstimator.EstimateTokens(new List<ChatMessage> { firstUserMessage });
        var remainingBudget = targetTokenBudget - firstUserTokens;

        if (remainingBudget <= 0)
            return new List<ChatMessage> { firstUserMessage };

        // Walk backwards from the end, adding turns until budget is exceeded
        var tailMessages = new List<ChatMessage>();
        long tailTokens = 0;

        for (int i = messages.Count - 1; i > firstUserIdx; i--)
        {
            var msgTokens = TokenEstimator.EstimateTokens(new List<ChatMessage> { messages[i] });

            if (tailTokens + msgTokens > remainingBudget)
                break;

            tailMessages.Insert(0, messages[i]);
            tailTokens += msgTokens;
        }

        // Build result: first user message + compaction marker + tail
        var result = new List<ChatMessage> { firstUserMessage };

        var skipped = messages.Count - 1 - firstUserIdx - tailMessages.Count;
        if (skipped > 0)
        {
            result.Add(new UserMessage(
                $"[Context compacted: {skipped} messages summarized. Recent context follows.]"));
        }

        result.AddRange(tailMessages);
        return result;
    }

    /// <summary>
    /// Check if compaction is needed based on estimated tokens vs max context.
    /// Triggers at 80% of max context tokens.
    /// </summary>
    public static bool NeedsCompaction(List<ChatMessage> messages, int? maxContextTokens)
    {
        if (!maxContextTokens.HasValue || maxContextTokens <= 0)
            return false;

        var estimated = TokenEstimator.EstimateTokens(messages);
        return estimated > maxContextTokens.Value * 0.8;
    }

    /// <summary>
    /// Get the target token budget for compaction (60% of max context).
    /// </summary>
    public static long GetTargetBudget(int maxContextTokens)
    {
        return (long)(maxContextTokens * 0.6);
    }
}
