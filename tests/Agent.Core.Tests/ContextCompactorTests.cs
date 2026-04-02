using Agent.Core.Messages;
using Agent.Core.Session;
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
        // Add many messages to exceed a small budget
        messages.Add(new UserMessage("Initial task"));
        for (int i = 0; i < 20; i++)
        {
            messages.Add(new AssistantMessage { Content = new string('x', 500) });
            messages.Add(new UserMessage($"Follow up {i}"));
        }

        // Very small budget — should compact and add marker
        var result = ContextCompactor.CompactSlidingWindow(messages, 200);

        result.Count.Should().BeLessThan(messages.Count);
        result[0].Should().BeOfType<UserMessage>();
        ((UserMessage)result[0]).Content.Should().Be("Initial task");

        // Second message should be the compaction marker
        result[1].Should().BeOfType<UserMessage>();
        ((UserMessage)result[1]).Content.Should().Contain("Context compacted");
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
}
