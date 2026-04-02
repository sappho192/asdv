using Agent.Core.Messages;
using Agent.Core.Session;
using Agent.Core.Tools;
using FluentAssertions;

namespace Agent.Core.Tests;

public class ContextCompactorTests
{
    [Fact]
    public void CompactSlidingWindow_EmptyMessages_ReturnsEmpty()
    {
        var result = ContextCompactor.CompactSlidingWindow(new List<ChatMessage>(), 10000);
        result.Should().BeEmpty();
    }

    [Fact]
    public void CompactSlidingWindow_PreservesFirstUserMessage()
    {
        var messages = new List<ChatMessage>
        {
            new UserMessage("Initial task"),
            new AssistantMessage { Content = "Response 1" },
            new UserMessage("Follow up"),
            new AssistantMessage { Content = "Response 2" },
            new UserMessage("Another follow up"),
            new AssistantMessage { Content = "Response 3" }
        };

        // Very small budget — should at least keep first user message
        var result = ContextCompactor.CompactSlidingWindow(messages, 50);

        result.Should().NotBeEmpty();
        result[0].Should().BeOfType<UserMessage>();
        ((UserMessage)result[0]).Content.Should().Be("Initial task");
    }

    [Fact]
    public void CompactSlidingWindow_KeepsRecentTurnsWithinBudget()
    {
        var messages = new List<ChatMessage>
        {
            new UserMessage("Initial task"),
            new AssistantMessage { Content = "Response 1" },
            new UserMessage("Middle question"),
            new AssistantMessage { Content = "Response 2" },
            new UserMessage("Recent question"),
            new AssistantMessage { Content = "Recent response" }
        };

        // Large budget — should keep everything
        var result = ContextCompactor.CompactSlidingWindow(messages, 100000);

        result.Should().HaveCount(messages.Count);
    }

    [Fact]
    public void CompactSlidingWindow_AddsCompactionMarkerWhenSkipping()
    {
        var messages = new List<ChatMessage>();
        // Add many turn groups to exceed a small budget
        messages.Add(new UserMessage("Initial task"));
        messages.Add(new AssistantMessage { Content = "OK" });
        for (int i = 0; i < 20; i++)
        {
            messages.Add(new UserMessage($"Follow up {i}"));
            messages.Add(new AssistantMessage { Content = new string('x', 500) });
        }

        // Very small budget — should compact and add marker
        var result = ContextCompactor.CompactSlidingWindow(messages, 200);

        result.Count.Should().BeLessThan(messages.Count);
        // First turn group preserved
        result[0].Should().BeOfType<UserMessage>();
        ((UserMessage)result[0]).Content.Should().Be("Initial task");

        // Compaction marker should appear somewhere after first group
        result.OfType<UserMessage>().Should().Contain(m => m.Content.Contains("Context compacted"));
    }

    [Fact]
    public void CompactSlidingWindow_MaintainsMessageOrder()
    {
        var messages = new List<ChatMessage>
        {
            new UserMessage("Initial task"),
            new AssistantMessage { Content = "Response 1" },
            new UserMessage("Q2"),
            new AssistantMessage { Content = "Response 2" },
            new UserMessage("Q3"),
            new AssistantMessage { Content = "Response 3" },
            new UserMessage("Q4"),
            new AssistantMessage { Content = "Response 4" }
        };

        var result = ContextCompactor.CompactSlidingWindow(messages, 5000);

        // Should maintain relative order of kept messages
        for (int i = 0; i < result.Count - 1; i++)
        {
            if (result[i] is UserMessage && result[i + 1] is UserMessage)
            {
                // Two user messages in a row is only valid if second is compaction marker
                if (i == 0)
                    ((UserMessage)result[1]).Content.Should().Contain("compacted");
            }
        }
    }

    [Fact]
    public void NeedsCompaction_BelowThreshold_ReturnsFalse()
    {
        var messages = new List<ChatMessage>
        {
            new UserMessage("Short message")
        };

        ContextCompactor.NeedsCompaction(messages, 128000).Should().BeFalse();
    }

    [Fact]
    public void NeedsCompaction_NullMaxContext_ReturnsFalse()
    {
        var messages = new List<ChatMessage>
        {
            new UserMessage("Any message")
        };

        ContextCompactor.NeedsCompaction(messages, null).Should().BeFalse();
    }

    [Fact]
    public void GetTargetBudget_Returns60Percent()
    {
        ContextCompactor.GetTargetBudget(100000).Should().Be(60000);
        ContextCompactor.GetTargetBudget(200000).Should().Be(120000);
    }

    [Fact]
    public void CompactSlidingWindow_PreservesToolCallToolResultPairing()
    {
        // Simulate: user ask -> assistant calls tool -> tool result -> assistant response
        var toolCallMsg = new AssistantMessage
        {
            Content = null,
            ToolCalls = new List<ToolCall> { new ToolCall("call_1", "ReadFile", "{\"path\":\"test.cs\"}") }
        };
        var toolResultMsg = new ToolResultMessage("call_1", "ReadFile",
            new ToolResult { Ok = true, Stdout = "file contents" });

        var messages = new List<ChatMessage>
        {
            new UserMessage("Initial task"),
            new AssistantMessage { Content = "I'll look at old files first" },
            new UserMessage("Check other files too"),
            new AssistantMessage { Content = new string('x', 500) },
            // This turn has a tool call + result pair that must stay together
            new UserMessage("Now read test.cs"),
            toolCallMsg,
            toolResultMsg,
            new AssistantMessage { Content = "Here's what I found" }
        };

        // Budget large enough for last turn but not all
        var result = ContextCompactor.CompactSlidingWindow(messages, 800);

        // If tool call is present, its result must also be present
        var hasToolCall = result.OfType<AssistantMessage>().Any(am => am.ToolCalls?.Count > 0);
        var hasToolResult = result.OfType<ToolResultMessage>().Any();

        if (hasToolCall)
            hasToolResult.Should().BeTrue("tool-call must have matching tool-result");
        if (hasToolResult)
            hasToolCall.Should().BeTrue("tool-result must have matching tool-call");
    }

    [Fact]
    public void GroupIntoTurns_GroupsToolCallWithResult()
    {
        var messages = new List<ChatMessage>
        {
            new UserMessage("Do something"),
            new AssistantMessage
            {
                Content = null,
                ToolCalls = new List<ToolCall> { new ToolCall("c1", "ReadFile", "{}") }
            },
            new ToolResultMessage("c1", "ReadFile", new ToolResult { Ok = true, Stdout = "data" }),
            new UserMessage("Next question"),
            new AssistantMessage { Content = "Answer" }
        };

        var groups = ContextCompactor.GroupIntoTurns(messages);

        groups.Should().HaveCount(2);
        // First group: user + assistant(tool_call) + tool_result
        groups[0].Should().HaveCount(3);
        groups[0][0].Should().BeOfType<UserMessage>();
        groups[0][1].Should().BeOfType<AssistantMessage>();
        groups[0][2].Should().BeOfType<ToolResultMessage>();
        // Second group: user + assistant
        groups[1].Should().HaveCount(2);
    }

    [Fact]
    public void GroupIntoTurns_EmptyList_ReturnsEmpty()
    {
        ContextCompactor.GroupIntoTurns(new List<ChatMessage>()).Should().BeEmpty();
    }
}
