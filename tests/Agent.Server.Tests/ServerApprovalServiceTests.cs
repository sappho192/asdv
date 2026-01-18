using System.Threading.Channels;
using Agent.Server.Models;
using Agent.Server.Services;
using FluentAssertions;

namespace Agent.Server.Tests;

public class ServerApprovalServiceTests
{
    [Fact]
    public async Task RequestApprovalAsync_WritesEventAndResolves()
    {
        var service = new ServerApprovalService();
        var channel = Channel.CreateUnbounded<ServerEvent>();
        service.AttachChannel(channel.Writer);

        var approvalTask = service.RequestApprovalAsync("RunCommand", "{\"exe\":\"rm\"}", "call-1");

        channel.Reader.TryRead(out var evt).Should().BeTrue();
        evt.Should().BeOfType<ApprovalRequiredEvent>();

        var approvalEvent = (ApprovalRequiredEvent)evt!;
        approvalEvent.CallId.Should().Be("call-1");
        approvalEvent.Tool.Should().Be("RunCommand");

        service.TryResolve("call-1", true).Should().BeTrue();
        (await approvalTask).Should().BeTrue();
    }
}
