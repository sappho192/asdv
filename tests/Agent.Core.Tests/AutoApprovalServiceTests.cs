using Agent.Core.Approval;
using FluentAssertions;

namespace Agent.Core.Tests;

public class AutoApprovalServiceTests
{
    [Fact]
    public async Task RequestApprovalAsync_AlwaysReturnsTrue()
    {
        var service = new AutoApprovalService();

        var result = await service.RequestApprovalAsync("RunCommand", "{}", "call1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RequestApprovalAsync_WithCancellation_StillReturnsTrue()
    {
        var service = new AutoApprovalService();
        using var cts = new CancellationTokenSource();

        var result = await service.RequestApprovalAsync("ApplyPatch", "{}", null, cts.Token);

        result.Should().BeTrue();
    }
}
