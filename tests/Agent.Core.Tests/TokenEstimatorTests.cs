using Agent.Core.Messages;
using Agent.Core.Session;
using FluentAssertions;

namespace Agent.Core.Tests;

public class TokenEstimatorTests
{
    [Fact]
    public void EstimateTokens_EmptyString_ReturnsZero()
    {
        TokenEstimator.EstimateTokens("").Should().Be(0);
    }

    [Fact]
    public void EstimateTokens_NullString_ReturnsZero()
    {
        TokenEstimator.EstimateTokens((string)null!).Should().Be(0);
    }

    [Fact]
    public void EstimateTokens_EnglishText_ReasonableEstimate()
    {
        // "Hello, world!" is typically 4 tokens with cl100k_base
        var tokens = TokenEstimator.EstimateTokens("Hello, world!");
        tokens.Should().BeGreaterThan(0).And.BeLessThan(10);
    }

    [Fact]
    public void EstimateTokens_CJKText_HigherThanEnglish()
    {
        // CJK text uses more tokens per character than English
        var englishTokens = TokenEstimator.EstimateTokens("Hello world, this is a test.");
        var koreanTokens = TokenEstimator.EstimateTokens("안녕하세요, 이것은 테스트입니다.");

        // Korean should use relatively more tokens per character
        var englishRatio = (double)englishTokens / "Hello world, this is a test.".Length;
        var koreanRatio = (double)koreanTokens / "안녕하세요, 이것은 테스트입니다.".Length;

        koreanRatio.Should().BeGreaterThan(englishRatio);
    }

    [Fact]
    public void EstimateTokens_Messages_IncludesOverhead()
    {
        var messages = new List<ChatMessage>
        {
            new UserMessage("Hello"),
            new AssistantMessage { Content = "Hi there!" }
        };

        var tokens = TokenEstimator.EstimateTokens(messages);

        // Should be more than just the text tokens (includes per-message overhead)
        var textOnly = TokenEstimator.EstimateTokens("Hello") + TokenEstimator.EstimateTokens("Hi there!");
        tokens.Should().BeGreaterThan(textOnly);
    }

    [Fact]
    public void GetBudgetDisplay_WithMaxContext_ShowsPercentage()
    {
        var state = new SessionState
        {
            SessionId = "test",
            ProviderName = "openai",
            ModelName = "gpt-4o",
            EstimatedInputTokens = 10000,
            EstimatedOutputTokens = 2000,
            MaxContextTokens = 128000
        };

        var display = TokenEstimator.GetBudgetDisplay(state);

        display.Should().Contain("10.0k");
        display.Should().Contain("128.0k");
        display.Should().Contain("%");
    }

    [Fact]
    public void GetBudgetDisplay_WithoutMaxContext_ShowsTotals()
    {
        var state = new SessionState
        {
            SessionId = "test",
            ProviderName = "local",
            ModelName = "custom",
            EstimatedInputTokens = 5000,
            EstimatedOutputTokens = 1000
        };

        var display = TokenEstimator.GetBudgetDisplay(state);

        display.Should().Contain("6.0k");
        display.Should().Contain("in:");
        display.Should().Contain("out:");
    }
}
