namespace Agent.Core.Approval;

public interface IApprovalService
{
    Task<bool> RequestApprovalAsync(string toolName, string argsJson, CancellationToken ct = default);
}
