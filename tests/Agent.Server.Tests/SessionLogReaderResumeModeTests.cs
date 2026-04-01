using Agent.Cli;
using Agent.Core.Messages;
using FluentAssertions;

namespace Agent.Server.Tests;

public class SessionLogReaderResumeModeTests
{
    private string CreateSessionLog(params string[] lines)
    {
        var path = Path.GetTempFileName();
        File.WriteAllLines(path, lines);
        return path;
    }

    private static string UserLine(string content) =>
        $"{{\"timestamp\":\"2024-01-01T00:00:00Z\",\"data\":{{\"type\":\"message\",\"role\":\"user\",\"content\":\"{content}\"}}}}";

    private static string AssistantLine(string content) =>
        $"{{\"timestamp\":\"2024-01-01T00:00:01Z\",\"data\":{{\"type\":\"message\",\"role\":\"assistant\",\"content\":\"{content}\"}}}}";

    private static string ToolLine(string callId, string toolName) =>
        $"{{\"timestamp\":\"2024-01-01T00:00:02Z\",\"data\":{{\"type\":\"message\",\"role\":\"tool\",\"callId\":\"{callId}\",\"toolName\":\"{toolName}\",\"result\":{{\"ok\":true}}}}}}";

    [Fact]
    public void LoadMessages_FullMode_ReturnsAllMessages()
    {
        var path = CreateSessionLog(UserLine("first"), AssistantLine("reply1"), UserLine("second"), AssistantLine("reply2"));
        try
        {
            var messages = SessionLogReader.LoadMessages(path, ResumeMode.Full, 0);
            messages.Should().HaveCount(4);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadMessages_LastN_ReturnsLastNTurns()
    {
        var path = CreateSessionLog(
            UserLine("turn1"), AssistantLine("reply1"),
            UserLine("turn2"), AssistantLine("reply2"),
            UserLine("turn3"), AssistantLine("reply3"));
        try
        {
            var messages = SessionLogReader.LoadMessages(path, ResumeMode.LastN, 2);

            messages.Should().HaveCount(4); // last 2 turns = 4 messages
            messages[0].Should().BeOfType<UserMessage>();
            ((UserMessage)messages[0]).Content.Should().Be("turn2");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadMessages_LastN_LargerThanTotal_ReturnsAll()
    {
        var path = CreateSessionLog(UserLine("only"), AssistantLine("reply"));
        try
        {
            var messages = SessionLogReader.LoadMessages(path, ResumeMode.LastN, 10);
            messages.Should().HaveCount(2);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadMessages_Summary_ReturnsSingleMessage()
    {
        var path = CreateSessionLog(
            UserLine("do something"),
            AssistantLine("ok"),
            ToolLine("c1", "ReadFile"),
            UserLine("now fix it"),
            AssistantLine("done"));
        try
        {
            var messages = SessionLogReader.LoadMessages(path, ResumeMode.Summary, 0);

            messages.Should().HaveCount(1);
            messages[0].Should().BeOfType<UserMessage>();
            var summary = ((UserMessage)messages[0]).Content;
            summary.Should().Contain("2 user messages");
            summary.Should().Contain("ReadFile");
            summary.Should().Contain("now fix it"); // last user prompt
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadMessages_Summary_EmptySession_ReturnsEmpty()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "");
        try
        {
            var messages = SessionLogReader.LoadMessages(path, ResumeMode.Summary, 0);
            messages.Should().BeEmpty();
        }
        finally { File.Delete(path); }
    }
}
