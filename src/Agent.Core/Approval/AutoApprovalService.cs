namespace Agent.Core.Approval;

public sealed class AutoApprovalService : IApprovalService
{
    public Task<bool> RequestApprovalAsync(
        string toolName,
        string argsJson,
        string? callId = null,
        CancellationToken ct = default)
        => Task.FromResult(true);
}
