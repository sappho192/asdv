using Agent.Core.Messages;
using Agent.Server.Services;
using FluentAssertions;

namespace Agent.Server.Tests;

public class SessionLogReaderTests
{
    [Fact]
    public void LoadMessages_ParsesUserAssistantToolMessages()
    {
        var tempPath = Path.GetTempFileName();
        try
        {
            var lines = new[]
            {
                "{\"timestamp\":\"2024-01-01T00:00:00Z\",\"data\":{\"type\":\"message\",\"role\":\"user\",\"content\":\"hi\"}}",
                "{\"timestamp\":\"2024-01-01T00:00:01Z\",\"data\":{\"type\":\"message\",\"role\":\"assistant\",\"content\":\"ok\",\"toolCalls\":[{\"callId\":\"c1\",\"name\":\"ReadFile\",\"argsJson\":\"{}\"}]}}",
                "{\"timestamp\":\"2024-01-01T00:00:02Z\",\"data\":{\"type\":\"message\",\"role\":\"tool\",\"callId\":\"c1\",\"toolName\":\"ReadFile\",\"result\":{\"ok\":true}}}"
            };

            File.WriteAllLines(tempPath, lines);

            var messages = SessionLogReader.LoadMessages(tempPath);

            messages.Should().HaveCount(3);
            messages[0].Should().BeOfType<UserMessage>();
            ((UserMessage)messages[0]).Content.Should().Be("hi");

            messages[1].Should().BeOfType<AssistantMessage>();
            var assistant = (AssistantMessage)messages[1];
            assistant.Content.Should().Be("ok");
            assistant.ToolCalls.Should().NotBeNull();
            assistant.ToolCalls!.Should().HaveCount(1);
            assistant.ToolCalls![0].CallId.Should().Be("c1");

            messages[2].Should().BeOfType<ToolResultMessage>();
            var toolMessage = (ToolResultMessage)messages[2];
            toolMessage.CallId.Should().Be("c1");
            toolMessage.ToolName.Should().Be("ReadFile");
            toolMessage.Result.Ok.Should().BeTrue();
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
