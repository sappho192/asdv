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
    /// with turn groups from the end. Turn groups preserve tool-call/tool-result
    /// pairing to maintain valid message history for providers.
    /// </summary>
    public static List<ChatMessage> CompactSlidingWindow(
        List<ChatMessage> messages,
        long targetTokenBudget)
    {
        if (messages.Count == 0)
            return messages;

        // Group messages into turns (user → assistant → tool_results)
        var turnGroups = GroupIntoTurns(messages);
        if (turnGroups.Count == 0)
            return messages;

        // Always keep the first turn group for task context
        var firstGroup = turnGroups[0];
        var firstGroupTokens = TokenEstimator.EstimateTokens(firstGroup);
        var remainingBudget = targetTokenBudget - firstGroupTokens;

        if (remainingBudget <= 0)
            return new List<ChatMessage>(firstGroup);

        // Walk backwards from the end, adding turn groups until budget exceeded
        var tailGroups = new List<List<ChatMessage>>();
        long tailTokens = 0;

        for (int i = turnGroups.Count - 1; i > 0; i--)
        {
            var groupTokens = TokenEstimator.EstimateTokens(turnGroups[i]);

            if (tailTokens + groupTokens > remainingBudget)
                break;

            tailGroups.Insert(0, turnGroups[i]);
            tailTokens += groupTokens;
        }

        // Build result: first group + compaction marker + tail groups
        var result = new List<ChatMessage>(firstGroup);

        var skippedGroups = turnGroups.Count - 1 - tailGroups.Count;
        if (skippedGroups > 0)
        {
            var skippedMessages = turnGroups.Skip(1).Take(skippedGroups).Sum(g => g.Count);
            result.Add(new UserMessage(
                $"[Context compacted: {skippedMessages} messages from {skippedGroups} turns summarized. Recent context follows.]"));
        }

        foreach (var group in tailGroups)
            result.AddRange(group);

        return result;
    }

    /// <summary>
    /// Group messages into turn boundaries. A turn starts with a UserMessage.
    /// AssistantMessage + its ToolResultMessages are kept together.
    /// </summary>
    public static List<List<ChatMessage>> GroupIntoTurns(List<ChatMessage> messages)
    {
        var groups = new List<List<ChatMessage>>();
        var current = new List<ChatMessage>();

        foreach (var msg in messages)
        {
            if (msg is UserMessage && current.Count > 0)
            {
                groups.Add(current);
                current = new List<ChatMessage>();
            }
            current.Add(msg);
        }

        if (current.Count > 0)
            groups.Add(current);

        return groups;
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
